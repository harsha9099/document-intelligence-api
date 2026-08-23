using DocumentIntelligence.Api.Extractors;
using DocumentIntelligence.Api.LlmProviders;
using DocumentIntelligence.Api.Models;
using DocumentIntelligence.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddSingleton<IPdfExtractor, PdfExtractor>();
builder.Services.AddSingleton<IImageExtractor, ImageExtractor>();
builder.Services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

var app = builder.Build();

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var allowedExtensions = builder.Configuration
    .GetValue<string>("DocumentSettings:AllowedExtensions")?
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    ?? ["pdf", "png", "jpg", "jpeg", "tiff", "bmp", "webp"];

var maxFileSizeMb = builder.Configuration.GetValue("DocumentSettings:MaxFileSizeMb", 50);

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/extract", async (
    HttpRequest request,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new ErrorResponse { Error = "Request must be multipart/form-data" });

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");

    if (file == null || file.Length == 0)
        return Results.BadRequest(new ErrorResponse { Error = "No file provided" });

    var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
    if (!allowedExtensions.Contains(ext))
        return Results.BadRequest(new ErrorResponse
        {
            Error = $"File type '.{ext}' not supported",
            Detail = $"Allowed: {string.Join(", ", allowedExtensions)}"
        });

    if (file.Length > maxFileSizeMb * 1024 * 1024)
        return Results.BadRequest(new ErrorResponse { Error = $"File exceeds maximum size of {maxFileSizeMb}MB" });

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms, cancellationToken);
    var fileBytes = ms.ToArray();

    var provider = form["provider"].FirstOrDefault();
    var model = form["model"].FirstOrDefault();
    var hint = form["hint"].FirstOrDefault();
    var useVision = !bool.TryParse(form["use_vision"].FirstOrDefault(), out var v) || v;

    try
    {
        var result = await documentService.ProcessAsync(
            fileBytes, file.FileName, provider, model, hint, useVision, cancellationToken);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorResponse { Error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.UnprocessableEntity(new ErrorResponse
        {
            Error = "Document processing failed",
            Detail = ex.Message
        });
    }
})
.DisableAntiforgery();

app.MapPost("/extract/batch", async (
    HttpRequest request,
    IDocumentService documentService,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new ErrorResponse { Error = "Request must be multipart/form-data" });

    var form = await request.ReadFormAsync(cancellationToken);
    var files = form.Files.Where(f => f.Name == "files").ToList();

    if (files.Count == 0)
        return Results.BadRequest(new ErrorResponse { Error = "No files provided" });

    if (files.Count > 10)
        return Results.BadRequest(new ErrorResponse { Error = "Maximum 10 files per batch request" });

    var provider = form["provider"].FirstOrDefault();
    var hint = form["hint"].FirstOrDefault();
    var useVision = !bool.TryParse(form["use_vision"].FirstOrDefault(), out var v) || v;

    var results = new List<DocumentResponse>();

    foreach (var file in files)
    {
        if (file.Length > maxFileSizeMb * 1024 * 1024) continue;

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);

        try
        {
            var result = await documentService.ProcessAsync(
                ms.ToArray(), file.FileName, provider, null, hint, useVision, cancellationToken);
            results.Add(result);
        }
        catch
        {
            // Skip failed files in batch mode
        }
    }

    return Results.Ok(results);
})
.DisableAntiforgery();

app.Run();
