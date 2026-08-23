using System.Text.Json.Serialization;

namespace DocumentIntelligence.Api.Models;

public record ErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}
