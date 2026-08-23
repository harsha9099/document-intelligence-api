namespace DocumentIntelligence.Api.LlmProviders;

public interface ILlmProvider
{
    string Name { get; }
    string ModelUsed { get; }

    Task<Dictionary<string, object>> AnalyzeDocumentAsync(
        string? text = null,
        List<byte[]>? images = null,
        byte[]? rawFileBytes = null,
        string? mimeType = null,
        string? extractionHint = null,
        CancellationToken cancellationToken = default);
}
