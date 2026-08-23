using DocumentIntelligence.Api.Models;

namespace DocumentIntelligence.Api.Services;

public interface IExtractionPipeline
{
    Task<DocumentResponse> ExtractAsync(
        byte[] fileBytes,
        string filename,
        string? provider = null,
        string? model = null,
        string? hint = null,
        bool useVision = true,
        CancellationToken cancellationToken = default);
}
