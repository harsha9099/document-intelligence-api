namespace DocumentIntelligence.Api.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(string fileId, string filename, byte[] fileBytes, CancellationToken ct = default);
    Task<(byte[] Bytes, string Filename)?> GetAsync(string fileId, CancellationToken ct = default);
    Task<bool> DeleteAsync(string fileId, CancellationToken ct = default);
}
