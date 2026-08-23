namespace DocumentIntelligence.Api.Models;

public record ExtractRequest
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Hint { get; init; }
    public bool UseVision { get; init; } = true;
}
