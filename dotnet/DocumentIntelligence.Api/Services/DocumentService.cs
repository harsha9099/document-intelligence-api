using System.Diagnostics;
using System.Text.Json;
using DocumentIntelligence.Api.Extractors;
using DocumentIntelligence.Api.LlmProviders;
using DocumentIntelligence.Api.Models;

namespace DocumentIntelligence.Api.Services;

public class DocumentService : IDocumentService
{
    private readonly IPdfExtractor _pdfExtractor;
    private readonly IImageExtractor _imageExtractor;
    private readonly ILlmProviderFactory _llmFactory;
    private readonly ILogger<DocumentService> _logger;

    private static readonly HashSet<string> PdfExtensions = ["pdf"];
    private static readonly HashSet<string> ImageExtensions = ["png", "jpg", "jpeg", "tiff", "bmp", "webp"];

    public DocumentService(
        IPdfExtractor pdfExtractor,
        IImageExtractor imageExtractor,
        ILlmProviderFactory llmFactory,
        ILogger<DocumentService> logger)
    {
        _pdfExtractor = pdfExtractor;
        _imageExtractor = imageExtractor;
        _llmFactory = llmFactory;
        _logger = logger;
    }

    public async Task<DocumentResponse> ProcessAsync(
        byte[] fileBytes,
        string filename,
        string? provider = null,
        string? model = null,
        string? hint = null,
        bool useVision = true,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
        string? text = null;
        List<byte[]>? images = null;
        byte[]? rawFileForVision = null;
        string? mimeType = null;

        if (PdfExtensions.Contains(ext))
        {
            text = _pdfExtractor.ExtractText(fileBytes);
            _logger.LogDebug("PDF text extracted: {CharCount} chars from {FileName}", text?.Length ?? 0, filename);

            if (useVision)
            {
                rawFileForVision = fileBytes;
                mimeType = "application/pdf";
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogDebug("Scanned PDF detected, forcing vision path for {FileName}", filename);
                rawFileForVision = fileBytes;
                mimeType = "application/pdf";
            }
        }
        else if (ImageExtensions.Contains(ext))
        {
            if (useVision)
            {
                images = [_imageExtractor.PrepareForLlm(fileBytes)];
            }

            text = _imageExtractor.ExtractText(fileBytes);
            _logger.LogDebug("OCR extracted: {CharCount} chars from {FileName}", text?.Length ?? 0, filename);
        }

        if (images == null && rawFileForVision == null && (text == null || text.Trim().Length < 100))
        {
            _logger.LogDebug("Insufficient text, falling back to raw image vision for {FileName}", filename);
            images = [fileBytes];
        }

        var llm = _llmFactory.Create(provider, model, filename);
        _logger.LogInformation("Using provider={Provider} model={Model} for {FileName}",
            llm.Name, model ?? "default", filename);

        var sw = Stopwatch.StartNew();
        var result = await llm.AnalyzeDocumentAsync(
            text: string.IsNullOrWhiteSpace(text) ? null : text,
            images: images,
            rawFileBytes: rawFileForVision,
            mimeType: mimeType,
            extractionHint: hint,
            cancellationToken: cancellationToken);
        sw.Stop();

        _logger.LogInformation("LLM analysis completed in {ElapsedMs}ms for {FileName}", sw.ElapsedMilliseconds, filename);

        return MapToResponse(result, filename, text);
    }

    private static DocumentResponse MapToResponse(Dictionary<string, object> result, string filename, string? text)
    {
        DocumentQuality? quality = null;
        if (result.TryGetValue("quality", out var qualityObj) && qualityObj is JsonElement qualityEl)
        {
            quality = new DocumentQuality
            {
                Readable = qualityEl.TryGetProperty("readable", out var r) && r.GetBoolean(),
                Issues = qualityEl.TryGetProperty("issues", out var issues)
                    ? issues.EnumerateArray().Select(i => i.GetString() ?? "").ToList()
                    : []
            };
        }

        DocumentValidation? validation = null;
        if (result.TryGetValue("validation", out var valObj) && valObj is JsonElement valEl)
        {
            validation = new DocumentValidation
            {
                IsExpired = valEl.TryGetProperty("is_expired", out var exp) && exp.ValueKind != JsonValueKind.Null
                    ? exp.GetBoolean()
                    : null,
                ExpiryDate = valEl.TryGetProperty("expiry_date", out var ed) && ed.ValueKind != JsonValueKind.Null
                    ? ed.GetString()
                    : null,
                Issues = valEl.TryGetProperty("issues", out var vi)
                    ? vi.EnumerateArray().Select(i => i.GetString() ?? "").ToList()
                    : []
            };
        }

        Dictionary<string, object> content = [];
        if (result.TryGetValue("content", out var contentObj))
        {
            if (contentObj is JsonElement contentEl && contentEl.ValueKind == JsonValueKind.Object)
            {
                content = JsonSerializer.Deserialize<Dictionary<string, object>>(contentEl.GetRawText()) ?? [];
            }
            else if (contentObj is Dictionary<string, object> dict)
            {
                content = dict;
            }
        }

        return new DocumentResponse
        {
            Filename = filename,
            DocumentType = result.GetValueOrDefault("document_type")?.ToString() ?? "unknown",
            DocumentSubtype = result.GetValueOrDefault("document_subtype")?.ToString(),
            Title = result.GetValueOrDefault("title")?.ToString() ?? filename,
            Confidence = result.TryGetValue("confidence", out var conf)
                ? conf is JsonElement confEl ? confEl.GetDouble() : Convert.ToDouble(conf)
                : 0.0,
            Quality = quality,
            Content = content,
            Validation = validation,
            RawText = string.IsNullOrWhiteSpace(text) ? null : text
        };
    }
}
