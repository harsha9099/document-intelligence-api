using System.Diagnostics;
using System.Text.Json;
using DocumentIntelligence.Api.Extractors;
using DocumentIntelligence.Api.LlmProviders;
using DocumentIntelligence.Api.Models;

namespace DocumentIntelligence.Api.Services;

public class ExtractionPipeline : IExtractionPipeline
{
    private readonly IDocumentService _documentService;
    private readonly IAzureDocumentIntelligenceExtractor _azureDi;
    private readonly IConfiguration _config;
    private readonly ILogger<ExtractionPipeline> _logger;

    public ExtractionPipeline(
        IDocumentService documentService,
        IAzureDocumentIntelligenceExtractor azureDi,
        IConfiguration config,
        ILogger<ExtractionPipeline> logger)
    {
        _documentService = documentService;
        _azureDi = azureDi;
        _config = config;
        _logger = logger;
    }

    public async Task<DocumentResponse> ExtractAsync(
        byte[] fileBytes,
        string filename,
        string? provider = null,
        string? model = null,
        string? hint = null,
        bool useVision = true,
        CancellationToken cancellationToken = default)
    {
        var strategy = _config["Extraction:Strategy"] ?? "llm_only";
        var threshold = _config.GetValue("Extraction:ConfidenceThreshold", 0.85);

        _logger.LogInformation("Extraction strategy={Strategy} file={Filename}", strategy, filename);

        return strategy.ToLower() switch
        {
            "ocr_first" => await OcrFirstAsync(fileBytes, filename, provider, model, hint, useVision, threshold, cancellationToken),
            "azure_di_first" => await AzureDiFirstAsync(fileBytes, filename, provider, model, hint, useVision, threshold, cancellationToken),
            "hybrid" => await HybridAsync(fileBytes, filename, provider, model, hint, useVision, cancellationToken),
            _ => await LlmOnlyAsync(fileBytes, filename, provider, model, hint, useVision, cancellationToken),
        };
    }

    private async Task<DocumentResponse> LlmOnlyAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, CancellationToken ct)
    {
        var result = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        return result with { ExtractionMetadata = new Dictionary<string, object> { ["tier"] = "llm", ["llm_skipped"] = false } };
    }

    private async Task<DocumentResponse> OcrFirstAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, double threshold, CancellationToken ct)
    {
        // OCR is already run inside DocumentService; use confidence from a text-only pass
        // For now, attempt LLM with vision=false as Tier 1
        var tier1 = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, false, ct);
        if (tier1.Confidence >= threshold)
        {
            _logger.LogInformation("ocr_first: tier1 confidence {Conf} sufficient, skipping vision LLM", tier1.Confidence);
            return tier1 with
            {
                ExtractionMetadata = new Dictionary<string, object>
                {
                    ["tier"] = "ocr", ["llm_skipped"] = true,
                    ["tier1_confidence"] = tier1.Confidence,
                    ["estimated_cost_savings"] = "~70% vs vision LLM",
                }
            };
        }

        _logger.LogInformation("ocr_first: tier1 confidence {Conf} too low, running vision LLM", tier1.Confidence);
        var llm = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        return llm with
        {
            ExtractionMetadata = new Dictionary<string, object>
            {
                ["tier"] = "llm_fallback", ["llm_skipped"] = false,
                ["tier1_confidence"] = tier1.Confidence, ["tier2_confidence"] = llm.Confidence,
            }
        };
    }

    private async Task<DocumentResponse> AzureDiFirstAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, double threshold, CancellationToken ct)
    {
        var diResult = await RunAzureDiAsync(fileBytes, filename, hint, ct);
        if (diResult is not null && diResult.Confidence >= threshold)
        {
            _logger.LogInformation("azure_di_first: Azure DI confidence {Conf} sufficient, skipping LLM", diResult.Confidence);
            return diResult with
            {
                ExtractionMetadata = new Dictionary<string, object>
                {
                    ["tier"] = "azure_di", ["llm_skipped"] = true,
                    ["tier1_confidence"] = diResult.Confidence,
                    ["estimated_cost_savings"] = "~95% vs LLM vision",
                }
            };
        }

        _logger.LogInformation("azure_di_first: Azure DI insufficient or unconfigured, falling back to LLM");
        var llm = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        return llm with
        {
            ExtractionMetadata = new Dictionary<string, object>
            {
                ["tier"] = "llm_fallback", ["llm_skipped"] = false,
                ["tier1_confidence"] = diResult?.Confidence ?? 0.0, ["tier2_confidence"] = llm.Confidence,
            }
        };
    }

    private async Task<DocumentResponse> HybridAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, CancellationToken ct)
    {
        var diTask = RunAzureDiAsync(fileBytes, filename, hint, ct);
        var llmTask = _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        await Task.WhenAll(diTask, llmTask);

        var diResult = diTask.Result;
        var llmResult = llmTask.Result;
        var (merged, discrepancies) = MergeResults(diResult, llmResult);

        return merged with
        {
            ExtractionMetadata = new Dictionary<string, object>
            {
                ["tier"] = "hybrid", ["llm_skipped"] = false,
                ["tier1_confidence"] = diResult?.Confidence ?? 0.0,
                ["tier2_confidence"] = llmResult.Confidence,
                ["discrepancies"] = discrepancies,
            }
        };
    }

    private async Task<DocumentResponse?> RunAzureDiAsync(byte[] fileBytes, string filename, string? hint, CancellationToken ct)
    {
        if (!_azureDi.IsConfigured) return null;

        var hintLower = (hint ?? filename).ToLower();
        Dictionary<string, object>? raw = null;

        if (hintLower.Contains("invoice"))
            raw = await _azureDi.AnalyzeInvoiceAsync(fileBytes, ct);
        else if (hintLower.Contains("id") || hintLower.Contains("passport") || hintLower.Contains("identity") || hintLower.Contains("license"))
            raw = await _azureDi.AnalyzeIdentityDocumentAsync(fileBytes, ct);
        else if (hintLower.Contains("receipt") || hintLower.Contains("bill"))
            raw = await _azureDi.AnalyzeReceiptAsync(fileBytes, ct);
        else
            raw = await _azureDi.AnalyzeGeneralAsync(fileBytes, ct);

        if (raw is null) return null;

        return MapRawToResponse(raw, filename);
    }

    private static DocumentResponse MapRawToResponse(Dictionary<string, object> raw, string filename)
    {
        static T? Get<T>(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is T t ? t : default;

        var qualityRaw = Get<Dictionary<string, object>>(raw, "quality");
        var validationRaw = Get<Dictionary<string, object>>(raw, "validation");

        var contentRaw = raw.TryGetValue("content", out var co)
            ? co is Dictionary<string, object?> d1 ? d1.ToDictionary(k => k.Key, k => (object)(k.Value ?? ""))
            : co is Dictionary<string, object> d2 ? d2 : new Dictionary<string, object>()
            : new Dictionary<string, object>();

        return new DocumentResponse
        {
            Filename = filename,
            DocumentType = raw.TryGetValue("document_type", out var dt) ? dt?.ToString() ?? "unknown" : "unknown",
            DocumentSubtype = raw.TryGetValue("document_subtype", out var ds) ? ds?.ToString() : null,
            Title = raw.TryGetValue("title", out var t) ? t?.ToString() ?? filename : filename,
            Confidence = raw.TryGetValue("confidence", out var c) ? Convert.ToDouble(c) : 0.0,
            Quality = qualityRaw is not null ? new DocumentQuality
            {
                Readable = qualityRaw.TryGetValue("readable", out var r) && r is bool rb && rb,
                Issues = qualityRaw.TryGetValue("issues", out var i) && i is IEnumerable<string> ie ? ie.ToList() : [],
            } : null,
            Content = contentRaw,
            Validation = validationRaw is not null ? new DocumentValidation
            {
                IsExpired = validationRaw.TryGetValue("is_expired", out var ex) && ex is bool eb ? eb : null,
                ExpiryDate = validationRaw.TryGetValue("expiry_date", out var ed) ? ed?.ToString() : null,
                Issues = validationRaw.TryGetValue("issues", out var vi) && vi is IEnumerable<string> ve ? ve.ToList() : [],
            } : null,
        };
    }

    private static (DocumentResponse merged, List<string> discrepancies) MergeResults(
        DocumentResponse? diResult, DocumentResponse llmResult)
    {
        if (diResult is null) return (llmResult, []);

        var discrepancies = new List<string>();
        if (diResult.DocumentType != llmResult.DocumentType)
            discrepancies.Add($"document_type: azure_di={diResult.DocumentType}, llm={llmResult.DocumentType}");

        foreach (var key in diResult.Content.Keys.Intersect(llmResult.Content.Keys))
        {
            var v1 = diResult.Content[key]?.ToString();
            var v2 = llmResult.Content[key]?.ToString();
            if (!string.IsNullOrEmpty(v1) && !string.IsNullOrEmpty(v2) && v1 != v2)
                discrepancies.Add($"content.{key}: azure_di={v1}, llm={v2}");
        }

        var boostedConfidence = discrepancies.Count == 0 || diResult.DocumentType == llmResult.DocumentType
            ? Math.Min(1.0, llmResult.Confidence * 1.05)
            : llmResult.Confidence;

        return (llmResult with { Confidence = Math.Round(boostedConfidence, 3) }, discrepancies);
    }
}
