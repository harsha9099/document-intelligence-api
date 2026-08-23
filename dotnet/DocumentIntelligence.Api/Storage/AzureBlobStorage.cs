namespace DocumentIntelligence.Api.Storage;

/// <summary>Azure Blob Storage implementation — stub, not yet implemented.</summary>
public class AzureBlobStorage : IFileStorage
{
    public Task<string> SaveAsync(string fileId, string filename, byte[] fileBytes, CancellationToken ct = default)
        => throw new NotImplementedException("AzureBlobStorage not implemented");

    public Task<(byte[] Bytes, string Filename)?> GetAsync(string fileId, CancellationToken ct = default)
        => throw new NotImplementedException("AzureBlobStorage not implemented");

    public Task<bool> DeleteAsync(string fileId, CancellationToken ct = default)
        => throw new NotImplementedException("AzureBlobStorage not implemented");
}
