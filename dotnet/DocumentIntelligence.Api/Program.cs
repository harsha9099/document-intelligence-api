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
builder.Services.AddScoped<IDocumentService, DocumentService>();

// Persistence backend selection
var persistenceBackend = builder.Configuration.GetValue<string>("Persistence:Backend")
    ?? (builder.Environment.IsDevelopment() ? "memory" : "sqlite");

builder.Services.AddSingleton<IDocumentRepository>(_ => persistenceBackend.ToLower() switch
{
    "sqlite" => new SqliteDocumentRepository(
        builder.Configuration.GetConnectionString("Documents") ?? "Data Source=documents.db"),
    "cosmos" => new CosmosDocumentRepository(),
    "sql" => new SqlDocumentRepository(),
    "table_storage" => new TableStorageDocumentRepository(),
    _ => new InMemoryDocumentRepository()
});

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

// --- Shared extraction handler ---

async Task<IResult> HandleExtract(
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken,
    string? typeHint = null)
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
    var userHint = form["hint"].FirstOrDefault();
    var useVision = !bool.TryParse(form["use_vision"].FirstOrDefault(), out var v) || v;

    // Combine the route's type hint with any user-supplied hint
    var extractionHint = typeHint is not null
        ? string.IsNullOrWhiteSpace(userHint) ? typeHint : $"{typeHint}. {userHint}"
        : userHint;

    logger.LogInformation("Extract request: file={FileName} size={SizeKb}KB provider={Provider} typeHint={TypeHint}",
        file.FileName, file.Length / 1024, provider ?? "default", typeHint ?? "auto");

    try
    {
        var result = await documentService.ProcessAsync(
            fileBytes, file.FileName, provider, model, extractionHint, useVision, cancellationToken);

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
}

// --- Utility endpoints ---

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/documents", (
    IDocumentRepository repo,
    string? type = null,
    int limit = 100,
    int offset = 0) =>
    Results.Ok(repo.ListAll(limit, offset, type)));

app.MapGet("/documents/{id}", (string id, IDocumentRepository repo) =>
{
    var doc = repo.Get(id);
    return doc is not null ? Results.Ok(doc) : Results.NotFound(new ErrorResponse { Error = $"Document {id} not found" });
});

app.MapDelete("/documents/{id}", (string id, IDocumentRepository repo) =>
{
    var deleted = repo.Delete(id);
    return deleted ? Results.NoContent() : Results.NotFound(new ErrorResponse { Error = $"Document {id} not found" });
});

// --- Generic auto-detect ---

app.MapPost("/extract", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, logger, cancellationToken))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Auto-detect document type and extract structured data");

// --- Typed endpoints ---

app.MapPost("/extract/identity", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, logger, cancellationToken,
        "This is an identity document (passport, national ID, driver's license, or similar)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from identity documents: passport, national ID, driver's license, asylum permit");

app.MapPost("/extract/bank-statement", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, logger, cancellationToken,
        "This is a bank statement (current account, savings, credit card, or loan statement)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from bank statements: current account, savings, credit card, loan");

app.MapPost("/extract/proof-of-address", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, logger, cancellationToken,
        "This is a proof of address document (utility bill, municipal account, lease agreement, bank letter, or similar)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from proof of address: utility bill, municipal account, lease, bank letter");

app.MapPost("/extract/payslip", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, logger, cancellationToken,
        "This is a payslip or employment income document (monthly payslip, annual tax certificate, or employment letter)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from payslips: monthly payslip, annual tax certificate, employment letter");

app.MapPost("/extract/invoice", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, logger, cancellationToken,
        "This is an invoice (commercial invoice, proforma invoice, or tax invoice). Extract line items, totals, and payment details."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from invoices: commercial, proforma, tax invoice");

app.MapPost("/extract/bill", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, logger, cancellationToken,
        "This is a bill (phone bill, medical bill, subscription, or similar recurring charge document)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from bills: phone, medical, subscription, other recurring bills");

// --- Batch ---

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
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Batch extract from up to 10 files");

app.Run();
