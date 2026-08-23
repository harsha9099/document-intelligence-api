using DocumentIntelligence.Api.Extractors;
using DocumentIntelligence.Api.LlmProviders;
using DocumentIntelligence.Api.Middleware;
using DocumentIntelligence.Api.Models;
using DocumentIntelligence.Api.Repositories;
using DocumentIntelligence.Api.Services;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddSingleton<IPdfExtractor, PdfExtractor>();
builder.Services.AddSingleton<IImageExtractor, ImageExtractor>();
builder.Services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
builder.Services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000}ms";
});
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

app.MapGet("/documents", (IDocumentRepository repo) =>
    Results.Ok(repo.ListAll()));

app.MapGet("/documents/{id}", (string id, IDocumentRepository repo) =>
{
    var doc = repo.Get(id);
    return doc is not null ? Results.Ok(doc) : Results.NotFound(new ErrorResponse { Error = $"Document {id} not found" });
});

app.MapPost("/extract", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
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

    logger.LogInformation("Extract request: file={FileName} size={SizeKb}KB provider={Provider}",
        file.FileName, file.Length / 1024, provider ?? "default");

    try
    {
        var result = await documentService.ProcessAsync(
            fileBytes, file.FileName, provider, model, hint, useVision, cancellationToken);

        repository.Save(result);

        logger.LogInformation("Extraction complete: type={DocumentType} confidence={Confidence:F2}",
            result.DocumentType, result.Confidence);

        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning("Bad request: {Message}", ex.Message);
        return Results.BadRequest(new ErrorResponse { Error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Extraction failed for file {FileName}", file.FileName);
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
    IDocumentRepository repository,
    ILogger<Program> logger,
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

    logger.LogInformation("Batch extract request: fileCount={Count} provider={Provider}",
        files.Count, provider ?? "default");

    var results = new List<DocumentResponse>();

    foreach (var file in files)
    {
        if (file.Length > maxFileSizeMb * 1024 * 1024)
        {
            logger.LogWarning("Skipping {FileName}: exceeds size limit", file.FileName);
            continue;
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);

        try
        {
            var result = await documentService.ProcessAsync(
                ms.ToArray(), file.FileName, provider, null, hint, useVision, cancellationToken);
            repository.Save(result);
            results.Add(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch item failed: {FileName}", file.FileName);
        }
    }

    logger.LogInformation("Batch complete: {Succeeded}/{Total} succeeded", results.Count, files.Count);
    return Results.Ok(results);
})
.DisableAntiforgery();

app.Run();
