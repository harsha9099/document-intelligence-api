using System.Text.Json.Serialization;

namespace DocumentIntelligence.Api.Models;

public record DocumentResponse
{
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
