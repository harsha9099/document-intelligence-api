using System.Text.Json.Serialization;

namespace DocumentIntelligence.Api.Models;

public record DocumentResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; init; }

    [JsonPropertyName("file_content_type")]
    public string? FileContentType { get; init; }

    [JsonPropertyName("storage_path")]
    public string? StoragePath { get; init; }

    [JsonPropertyName("uploaded_at")]
    public string? UploadedAt { get; init; }

    [JsonPropertyName("processed_at")]
    public string? ProcessedAt { get; init; }

    [JsonPropertyName("processing_duration_ms")]
    public long ProcessingDurationMs { get; init; }

    [JsonPropertyName("provider_used")]
    public string? ProviderUsed { get; init; }

    [JsonPropertyName("model_used")]
    public string? ModelUsed { get; init; }

    [JsonPropertyName("page_count")]
    public int? PageCount { get; init; }

    [JsonPropertyName("document_type")]
    public required string DocumentType { get; init; }

    [JsonPropertyName("document_subtype")]
    public string? DocumentSubtype { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("quality")]
    public DocumentQuality? Quality { get; init; }

    [JsonPropertyName("content")]
    public required Dictionary<string, object> Content { get; init; }

    [JsonPropertyName("validation")]
    public DocumentValidation? Validation { get; init; }

    [JsonPropertyName("raw_text")]
    public string? RawText { get; init; }

    [JsonPropertyName("extraction_metadata")]
    public Dictionary<string, object>? ExtractionMetadata { get; init; }
}

public record DocumentQuality
{
    [JsonPropertyName("readable")]
    public bool Readable { get; init; }

    [JsonPropertyName("issues")]
    public List<string> Issues { get; init; } = [];
}

public record DocumentValidation
{
    [JsonPropertyName("is_expired")]
    public bool? IsExpired { get; init; }

    [JsonPropertyName("expiry_date")]
    public string? ExpiryDate { get; init; }

    [JsonPropertyName("issues")]
    public List<string> Issues { get; init; } = [];
}
