using DocumentIntelligence.Api.Models;

namespace DocumentIntelligence.Api.Repositories;

/// <summary>Azure Table Storage backend — not yet implemented.</summary>
public class TableStorageDocumentRepository : IDocumentRepository
{
    public void Save(DocumentResponse document) => throw new NotImplementedException("Azure Table Storage repository not yet implemented");
    public DocumentResponse? Get(string id) => throw new NotImplementedException("Azure Table Storage repository not yet implemented");
    public IReadOnlyList<DocumentResponse> ListAll(int limit = 100, int offset = 0, string? documentType = null) => throw new NotImplementedException("Azure Table Storage repository not yet implemented");
    public bool Delete(string id) => throw new NotImplementedException("Azure Table Storage repository not yet implemented");
}
