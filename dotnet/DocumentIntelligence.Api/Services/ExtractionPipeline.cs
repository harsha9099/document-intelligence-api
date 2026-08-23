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
        var strategy = (_config["Extraction:Strategy"] ?? "adaptive").ToLower();

        _logger.LogInformation("Extraction strategy={Strategy} file={Filename}", strategy, filename);

        return strategy switch
        {
            "llm_only"  => await LlmOnlyAsync(fileBytes, filename, provider, model, hint, useVision, cancellationToken),
            "ocr_first" => await OcrFirstAsync(fileBytes, filename, provider, model, hint, useVision, cancellationToken),
            "hybrid"    => await HybridAsync(fileBytes, filename, provider, model, hint, useVision, cancellationToken),
            _           => await AdaptiveAsync(fileBytes, filename, provider, model, hint, useVision, cancellationToken),
        };
    }

    // ── Strategies ─────────────────────────────────────────────────────────────

    private async Task<DocumentResponse> AdaptiveAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, CancellationToken ct)
    {
        var qualityTier = QualityDetector.Detect(filename, null);
        var prebuiltModel = SelectPrebuiltModel(hint, filename);
        _logger.LogInformation("adaptive_routing: file={File} quality={Quality} prebuilt={Model}",
            filename, qualityTier, prebuiltModel);

        // ALL documents go through Azure DI first (prebuilt models handle images/scans too)
        var diResult = await RunAzureDiAsync(fileBytes, filename, hint, ct);

        if (diResult is null)
        {
            var llm = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
            return llm with
            {
                ExtractionMetadata = Meta(new
                {
                    tier = "llm_fallback",
                    quality_tier = qualityTier.ToString().ToLower(),
                    prebuilt_model = prebuiltModel,
                    reason = "azure_di_unavailable",
                    llm_skipped = false,
                    field_completeness = true,
                    missing_fields = Array.Empty<string>()
                })
            };
        }

        if (diResult.Confidence < 0.65)
        {
            var llm = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
            return llm with
            {
                ExtractionMetadata = Meta(new
                {
                    tier = "llm_fallback",
                    quality_tier = qualityTier.ToString().ToLower(),
                    prebuilt_model = prebuiltModel,
                    reason = "azure_di_confidence_too_low",
                    tier1_confidence = diResult.Confidence,
                    llm_skipped = false,
                    field_completeness = false,
                    missing_fields = Array.Empty<string>()
                })
            };
        }

        var (isComplete, missingFields) = FieldRequirements.Check(diResult.DocumentType, diResult.Content);

        if (diResult.Confidence >= 0.85 && isComplete)
        {
            _logger.LogInformation("adaptive_routing: azure_di accepted confidence={Conf} type={Type} prebuilt={Model}",
                diResult.Confidence, diResult.DocumentType, prebuiltModel);
            return diResult with
            {
                ExtractionMetadata = Meta(new
                {
                    tier = "azure_di",
                    quality_tier = qualityTier.ToString().ToLower(),
                    prebuilt_model = prebuiltModel,
                    llm_skipped = true,
                    tier1_confidence = diResult.Confidence,
                    field_completeness = true,
                    missing_fields = Array.Empty<string>(),
                    estimated_cost_savings = "~95% vs LLM vision"
                })
            };
        }

        if (!isComplete)
        {
            _logger.LogInformation("adaptive_routing: missing fields {Fields}, falling back to LLM (prebuilt={Model})",
                string.Join(", ", missingFields), prebuiltModel);
            var llm = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
            return llm with
            {
                ExtractionMetadata = Meta(new
                {
                    tier = "llm_fallback",
                    quality_tier = qualityTier.ToString().ToLower(),
                    prebuilt_model = prebuiltModel,
                    reason = $"missing_fields: [{string.Join(", ", missingFields)}]",
                    tier1_confidence = diResult.Confidence,
                    llm_skipped = false,
                    field_completeness = false,
                    missing_fields = missingFields.ToArray()
                })
            };
        }

        // Medium confidence (0.65-0.85) + all fields present — accept with warning
        return diResult with
        {
            ExtractionMetadata = Meta(new
            {
                tier = "azure_di",
                quality_tier = qualityTier.ToString().ToLower(),
                prebuilt_model = prebuiltModel,
                llm_skipped = true,
                tier1_confidence = diResult.Confidence,
                confidence_warning = "medium",
                field_completeness = true,
                missing_fields = Array.Empty<string>(),
                estimated_cost_savings = "~95% vs LLM vision"
            })
        };
    }

    private static string SelectPrebuiltModel(string? hint, string filename)
    {
        var hintLower = (hint ?? filename).ToLower();
        if (hintLower.Contains("id") || hintLower.Contains("passport") || hintLower.Contains("identity")
            || hintLower.Contains("license") || hintLower.Contains("permit") || hintLower.Contains("national"))
            return "prebuilt-idDocument";
        if (hintLower.Contains("invoice"))
            return "prebuilt-invoice";
        if (hintLower.Contains("receipt") || hintLower.Contains("bill"))
            return "prebuilt-receipt";
        return "prebuilt-read";
    }

    private async Task<DocumentResponse> LlmOnlyAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, CancellationToken ct)
    {
        var result = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        return result with { ExtractionMetadata = Meta(new { tier = "llm", llm_skipped = false }) };
    }

    private async Task<DocumentResponse> OcrFirstAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, CancellationToken ct)
    {
        var threshold = _config.GetValue("Extraction:ConfidenceThreshold", 0.85);
        var tier1 = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, false, ct);

        if (tier1.Confidence >= threshold)
        {
            return tier1 with
            {
                ExtractionMetadata = Meta(new
                {
                    tier = "ocr", llm_skipped = true,
                    tier1_confidence = tier1.Confidence,
                    estimated_cost_savings = "~70% vs vision LLM"
                })
            };
        }

        var llm = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        return llm with
        {
            ExtractionMetadata = Meta(new
            {
                tier = "llm_fallback", llm_skipped = false,
                tier1_confidence = tier1.Confidence, tier2_confidence = llm.Confidence
            })
        };
    }

    private async Task<DocumentResponse> HybridAsync(
        byte[] fileBytes, string filename, string? provider, string? model,
        string? hint, bool useVision, CancellationToken ct)
    {
        var diTask  = RunAzureDiAsync(fileBytes, filename, hint, ct);
        var llmTask = _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        await Task.WhenAll(diTask, llmTask);

        var (merged, discrepancies) = MergeResults(diTask.Result, llmTask.Result);
        return merged with
        {
            ExtractionMetadata = Meta(new
            {
                tier = "hybrid", llm_skipped = false,
                tier1_confidence = diTask.Result?.Confidence ?? 0.0,
                tier2_confidence = llmTask.Result.Confidence,
                discrepancies
            })
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<DocumentResponse?> RunAzureDiAsync(
        byte[] fileBytes, string filename, string? hint, CancellationToken ct)
    {
        if (!_azureDi.IsConfigured) return null;

        var hintLower = (hint ?? filename).ToLower();
        Dictionary<string, object>? raw;

        if (hintLower.Contains("id") || hintLower.Contains("passport") || hintLower.Contains("identity")
            || hintLower.Contains("license") || hintLower.Contains("permit") || hintLower.Contains("national"))
            raw = await _azureDi.AnalyzeIdentityDocumentAsync(fileBytes, ct);
        else if (hintLower.Contains("invoice"))
            raw = await _azureDi.AnalyzeInvoiceAsync(fileBytes, ct);
        else if (hintLower.Contains("receipt") || hintLower.Contains("bill"))
            raw = await _azureDi.AnalyzeReceiptAsync(fileBytes, ct);
        else
            raw = await _azureDi.AnalyzeReadAsync(fileBytes, ct);

        return raw is null ? null : MapRawToResponse(raw, filename);
    }

    private static DocumentResponse MapRawToResponse(Dictionary<string, object> raw, string filename)
    {
        static T? Get<T>(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is T t ? t : default;

        var qualityRaw    = Get<Dictionary<string, object>>(raw, "quality");
        var validationRaw = Get<Dictionary<string, object>>(raw, "validation");
        var contentRaw = raw.TryGetValue("content", out var co) && co is JsonElement ce && ce.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(ce.GetRawText()) ?? []
            : co is Dictionary<string, object> cd ? cd : [];

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

        var boosted = discrepancies.Count == 0 || diResult.DocumentType == llmResult.DocumentType
            ? Math.Min(1.0, llmResult.Confidence * 1.05)
            : llmResult.Confidence;

        return (llmResult with { Confidence = Math.Round(boosted, 3) }, discrepancies);
    }

    private static Dictionary<string, object> Meta(object data) =>
        JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(data))!;
}
