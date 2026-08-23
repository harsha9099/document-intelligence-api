using System.Diagnostics;
using DocumentIntelligence.Api.Extractors;
using DocumentIntelligence.Api.LlmProviders;
using DocumentIntelligence.Api.Middleware;
using DocumentIntelligence.Api.Models;
using DocumentIntelligence.Api.Repositories;
using DocumentIntelligence.Api.Services;
using DocumentIntelligence.Api.Storage;
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

// Persistence backend
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

// File storage backend
var storageBackend = builder.Configuration.GetValue<string>("Storage:Backend") ?? "local";
var storagePath = builder.Configuration.GetValue<string>("Storage:Path") ?? "./uploads";

builder.Services.AddSingleton<IFileStorage>(_ => storageBackend.ToLower() switch
{
    "azure_blob" => new AzureBlobStorage(),
    _ => new LocalFileStorage(storagePath)
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
    IFileStorage fileStorage,
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

    var docId = Guid.NewGuid().ToString();
    var uploadedAt = DateTimeOffset.UtcNow.ToString("O");
    var contentType = file.ContentType ?? "application/octet-stream";

    // Page count for PDFs
    int? pageCount = null;
    if (ext == "pdf")
    {
        try { pageCount = PdfPageCount(fileBytes); } catch { /* ignore */ }
    }

    // Save original file
    string? storagePath2 = null;
    try { storagePath2 = await fileStorage.SaveAsync(docId, file.FileName, fileBytes, cancellationToken); }
    catch (Exception ex) { logger.LogWarning("File storage failed for {DocId}: {Error}", docId, ex.Message); }

    var provider = form["provider"].FirstOrDefault();
    var model = form["model"].FirstOrDefault();
    var userHint = form["hint"].FirstOrDefault();
    var useVision = !bool.TryParse(form["use_vision"].FirstOrDefault(), out var v) || v;

    var extractionHint = typeHint is not null
        ? string.IsNullOrWhiteSpace(userHint) ? typeHint : $"{typeHint}. {userHint}"
        : userHint;

    logger.LogInformation("Extract request: file={FileName} size={SizeKb}KB provider={Provider} typeHint={TypeHint}",
        file.FileName, file.Length / 1024, provider ?? "default", typeHint ?? "auto");

    try
    {
        var sw = Stopwatch.StartNew();
        var result = await documentService.ProcessAsync(
            fileBytes, file.FileName, provider, model, extractionHint, useVision, cancellationToken);
        sw.Stop();

        var processedAt = DateTimeOffset.UtcNow.ToString("O");

        // Determine the LLM provider/model that was used — get it from the factory
        // (DocumentService already logged it; we embed it in the response)
        var enriched = result with
        {
            Id = docId,
            FileSizeBytes = file.Length,
            FileContentType = contentType,
            StoragePath = storagePath2,
            UploadedAt = uploadedAt,
            ProcessedAt = processedAt,
            ProcessingDurationMs = sw.ElapsedMilliseconds,
            PageCount = pageCount,
        };

        repository.Save(enriched);

        logger.LogInformation("Extraction complete: docId={DocId} type={DocumentType} confidence={Confidence:F2} durationMs={DurationMs}",
            docId, enriched.DocumentType, enriched.Confidence, sw.ElapsedMilliseconds);

        return Results.Ok(enriched);
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

static int PdfPageCount(byte[] fileBytes)
{
    // Use PdfPig to count pages
    using var doc = UglyToad.PdfPig.PdfDocument.Open(fileBytes);
    return doc.NumberOfPages;
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

app.MapGet("/documents/{id}/file", async (string id, IDocumentRepository repo, IFileStorage fileStorage, CancellationToken ct) =>
{
    var doc = repo.Get(id);
    if (doc is null)
        return Results.NotFound(new ErrorResponse { Error = $"Document {id} not found" });

    var result = await fileStorage.GetAsync(id, ct);
    if (result is null)
        return Results.NotFound(new ErrorResponse { Error = "Original file not found in storage" });

    var (bytes, filename) = result.Value;
    return Results.File(bytes, doc.FileContentType ?? "application/octet-stream", filename);
});

app.MapDelete("/documents/{id}", async (string id, IDocumentRepository repo, IFileStorage fileStorage, CancellationToken ct) =>
{
    var deleted = repo.Delete(id);
    if (!deleted)
        return Results.NotFound(new ErrorResponse { Error = $"Document {id} not found" });
    await fileStorage.DeleteAsync(id, ct);
    return Results.NoContent();
});

// --- Generic auto-detect ---

app.MapPost("/extract", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, fileStorage, logger, cancellationToken))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Auto-detect document type and extract structured data");

// --- Typed endpoints ---

app.MapPost("/extract/identity", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, fileStorage, logger, cancellationToken,
        "This is an identity document (passport, national ID, driver's license, or similar)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from identity documents: passport, national ID, driver's license, asylum permit");

app.MapPost("/extract/bank-statement", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, fileStorage, logger, cancellationToken,
        "This is a bank statement (current account, savings, credit card, or loan statement)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from bank statements: current account, savings, credit card, loan");

app.MapPost("/extract/proof-of-address", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, fileStorage, logger, cancellationToken,
        "This is a proof of address document (utility bill, municipal account, lease agreement, bank letter, or similar)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from proof of address: utility bill, municipal account, lease, bank letter");

app.MapPost("/extract/payslip", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, fileStorage, logger, cancellationToken,
        "This is a payslip or employment income document (monthly payslip, annual tax certificate, or employment letter)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from payslips: monthly payslip, annual tax certificate, employment letter");

app.MapPost("/extract/invoice", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, fileStorage, logger, cancellationToken,
        "This is an invoice (commercial invoice, proforma invoice, or tax invoice). Extract line items, totals, and payment details."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from invoices: commercial, proforma, tax invoice");

app.MapPost("/extract/bill", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
    await HandleExtract(request, documentService, repository, fileStorage, logger, cancellationToken,
        "This is a bill (phone bill, medical bill, subscription, or similar recurring charge document)."))
.DisableAntiforgery()
.WithTags("Extraction")
.WithSummary("Extract from bills: phone, medical, subscription, other recurring bills");

// --- Batch ---

app.MapPost("/extract/batch", async (
    HttpRequest request,
    IDocumentService documentService,
    IDocumentRepository repository,
    IFileStorage fileStorage,
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

        // Build a synthetic single-file request for reuse
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        var docId = Guid.NewGuid().ToString();
        var uploadedAt = DateTimeOffset.UtcNow.ToString("O");

        string? storagePath3 = null;
        try { storagePath3 = await fileStorage.SaveAsync(docId, file.FileName, fileBytes, cancellationToken); }
        catch { /* storage failure shouldn't abort batch */ }

        try
        {
            var sw = Stopwatch.StartNew();
            var result = await documentService.ProcessAsync(
                fileBytes, file.FileName, provider, null, hint, useVision, cancellationToken);
            sw.Stop();

            var enriched = result with
            {
                Id = docId,
                FileSizeBytes = file.Length,
                FileContentType = file.ContentType ?? "application/octet-stream",
                StoragePath = storagePath3,
                UploadedAt = uploadedAt,
                ProcessedAt = DateTimeOffset.UtcNow.ToString("O"),
                ProcessingDurationMs = sw.ElapsedMilliseconds,
            };

            repository.Save(enriched);
            results.Add(enriched);
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
