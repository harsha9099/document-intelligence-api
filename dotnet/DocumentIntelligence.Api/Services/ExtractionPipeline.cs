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
        var docTypeHint = ResolveDocumentType(hint);

        _logger.LogInformation("adaptive_routing: file={File} quality={Quality} type_hint={TypeHint}",
            filename, qualityTier, docTypeHint ?? "auto");

        // ── TIER 1: prebuilt-read + pattern matching (~$0.001/page) ──
        var readText = await GetReadTextAsync(fileBytes, filename, ct);
        double? tier1Confidence = null;

        if (!string.IsNullOrWhiteSpace(readText))
        {
            var patternResult = PatternEngine.Extract(readText, docTypeHint);
            var detectedType = patternResult.DetectedType ?? docTypeHint;
            tier1Confidence = patternResult.OverallConfidence;

            if (detectedType is not null)
            {
                var (t1Complete, _) = FieldRequirements.Check(detectedType, patternResult.Fields);

                if (t1Complete && patternResult.OverallConfidence >= 0.80)
                {
                    _logger.LogInformation("tier1_accepted: patterns matched file={File} confidence={Conf} matched={Matched}",
                        filename, patternResult.OverallConfidence, patternResult.PatternsMatched);

                    return BuildPatternResponse(patternResult, detectedType, filename, readText) with
                    {
                        ExtractionMetadata = Meta(new
                        {
                            tier = "pattern_match",
                            quality_tier = qualityTier.ToString().ToLower(),
                            prebuilt_model = "prebuilt-read",
                            llm_skipped = true,
                            specialized_model_skipped = true,
                            tier1_confidence = patternResult.OverallConfidence,
                            patterns_matched = patternResult.PatternsMatched,
                            patterns_attempted = patternResult.PatternsAttempted,
                            field_completeness = true,
                            missing_fields = Array.Empty<string>(),
                            estimated_cost_savings = "~99% vs LLM vision"
                        })
                    };
                }

                _logger.LogInformation("tier1_partial: escalating file={File} matched={Matched}/{Total}",
                    filename, patternResult.PatternsMatched, patternResult.PatternsAttempted);
            }
        }

        // ── TIER 2: specialized prebuilt model (~$0.01/page) ──
        var prebuiltModel = SelectPrebuiltModel(docTypeHint);
        var diResult = await RunAzureDiAsync(fileBytes, filename, hint, ct);

        if (diResult is not null)
        {
            var (t2Complete, t2Missing) = FieldRequirements.Check(diResult.DocumentType, diResult.Content);

            if (diResult.Confidence >= 0.85 && t2Complete)
            {
                _logger.LogInformation("tier2_accepted: specialized model file={File} confidence={Conf} model={Model}",
                    filename, diResult.Confidence, prebuiltModel);
                return diResult with
                {
                    ExtractionMetadata = Meta(new
                    {
                        tier = "azure_di_specialized",
                        quality_tier = qualityTier.ToString().ToLower(),
                        prebuilt_model = prebuiltModel,
                        llm_skipped = true,
                        specialized_model_skipped = false,
                        tier1_confidence = tier1Confidence,
                        tier2_confidence = diResult.Confidence,
                        field_completeness = true,
                        missing_fields = Array.Empty<string>(),
                        estimated_cost_savings = "~95% vs LLM vision"
                    })
                };
            }

            if (diResult.Confidence >= 0.65 && t2Complete)
            {
                return diResult with
                {
                    ExtractionMetadata = Meta(new
                    {
                        tier = "azure_di_specialized",
                        quality_tier = qualityTier.ToString().ToLower(),
                        prebuilt_model = prebuiltModel,
                        llm_skipped = true,
                        specialized_model_skipped = false,
                        tier1_confidence = tier1Confidence,
                        tier2_confidence = diResult.Confidence,
                        confidence_warning = "medium",
                        field_completeness = true,
                        missing_fields = Array.Empty<string>(),
                        estimated_cost_savings = "~95% vs LLM vision"
                    })
                };
            }

            _logger.LogInformation("tier2_insufficient: escalating to LLM file={File} confidence={Conf} missing={Missing}",
                filename, diResult.Confidence, string.Join(", ", t2Missing));
        }

        // ── TIER 3: LLM Vision (last resort) ──
        var llmResult = await _documentService.ProcessAsync(fileBytes, filename, provider, model, hint, useVision, ct);
        return llmResult with
        {
            ExtractionMetadata = Meta(new
            {
                tier = "llm_fallback",
                quality_tier = qualityTier.ToString().ToLower(),
                prebuilt_model = prebuiltModel,
                llm_skipped = false,
                specialized_model_skipped = false,
                tier1_confidence = tier1Confidence,
                tier2_confidence = diResult?.Confidence,
                tier3_confidence = llmResult.Confidence,
                reason = BuildFallbackReason(diResult, readText),
                field_completeness = true,
                missing_fields = Array.Empty<string>()
            })
        };
    }

    private async Task<string?> GetReadTextAsync(byte[] fileBytes, string filename, CancellationToken ct)
    {
        if (!_azureDi.IsConfigured) return null;
        var raw = await _azureDi.AnalyzeReadAsync(fileBytes, ct);
        if (raw is null) return null;
        return raw.TryGetValue("content", out var content) && content is Dictionary<string, object> c
            && c.TryGetValue("raw_text", out var text) ? text?.ToString() : null;
    }

    private static string? ResolveDocumentType(string? hint)
    {
        if (string.IsNullOrEmpty(hint)) return null;
        var h = hint.ToLower();
        if (h.Contains("identity") || h.Contains("id") || h.Contains("passport") || h.Contains("license"))
            return "identity_document";
        if (h.Contains("bank") || h.Contains("statement"))
            return "bank_statement";
        if (h.Contains("invoice"))
            return "invoice";
        if (h.Contains("payslip") || h.Contains("pay slip"))
            return "payslip";
        if (h.Contains("bill") || h.Contains("receipt"))
            return "bill";
        if (h.Contains("proof") || h.Contains("address") || h.Contains("utility"))
            return "proof_of_address";
        return null;
    }

    private static string SelectPrebuiltModel(string? docTypeHint)
    {
        return docTypeHint switch
        {
            "identity_document" => "prebuilt-idDocument",
            "invoice" => "prebuilt-invoice",
            "bill" => "prebuilt-receipt",
            _ => "prebuilt-read"
        };
    }

    private static DocumentResponse BuildPatternResponse(
        PatternExtractionResult patternResult, string documentType, string filename, string rawText)
    {
        return new DocumentResponse
        {
            Filename = filename,
            DocumentType = documentType,
            DocumentSubtype = null,
            Title = $"{documentType.Replace("_", " ")} ({filename})",
            Confidence = patternResult.OverallConfidence,
            Quality = new DocumentQuality { Readable = true, Issues = [] },
            Content = patternResult.Fields,
            Validation = new DocumentValidation { IsExpired = null, ExpiryDate = null, Issues = [] },
        };
    }

    private static string BuildFallbackReason(DocumentResponse? diResult, string? readText)
    {
        if (string.IsNullOrWhiteSpace(readText)) return "no_text_extracted";
        if (diResult is null) return "azure_di_unavailable";
        if (diResult.Confidence < 0.65) return "all_tiers_low_confidence";
        return "missing_required_fields";
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
