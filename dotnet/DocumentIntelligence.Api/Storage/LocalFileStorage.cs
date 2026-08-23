namespace DocumentIntelligence.Api.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;

    public LocalFileStorage(string basePath = "./uploads")
    {
        _basePath = basePath;
    }

    public async Task<string> SaveAsync(string fileId, string filename, byte[] fileBytes, CancellationToken ct = default)
    {
        var dir = Path.Combine(_basePath, fileId);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, filename);
        await File.WriteAllBytesAsync(filePath, fileBytes, ct);
        return filePath;
    }

    public async Task<(byte[] Bytes, string Filename)?> GetAsync(string fileId, CancellationToken ct = default)
    {
        var dir = Path.Combine(_basePath, fileId);
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir);
        if (files.Length == 0) return null;
        var bytes = await File.ReadAllBytesAsync(files[0], ct);
        return (bytes, Path.GetFileName(files[0]));
    }

    public Task<bool> DeleteAsync(string fileId, CancellationToken ct = default)
    {
        var dir = Path.Combine(_basePath, fileId);
        if (!Directory.Exists(dir)) return Task.FromResult(false);
        Directory.Delete(dir, recursive: true);
        return Task.FromResult(true);
    }
}
