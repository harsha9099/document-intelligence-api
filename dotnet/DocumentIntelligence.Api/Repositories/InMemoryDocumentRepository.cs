using System.Collections.Concurrent;
using DocumentIntelligence.Api.Models;

namespace DocumentIntelligence.Api.Repositories;

public class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<string, DocumentResponse> _store = new();

    public void Save(DocumentResponse document) =>
        _store[document.Id] = document;

    public DocumentResponse? Get(string id) =>
        _store.TryGetValue(id, out var doc) ? doc : null;

    public IReadOnlyList<DocumentResponse> ListAll(int limit = 100, int offset = 0, string? documentType = null)
    {
        var query = _store.Values.AsEnumerable();
        if (documentType is not null)
            query = query.Where(d => d.DocumentType == documentType);
        return query.Skip(offset).Take(limit).ToList().AsReadOnly();
    }

    public bool Delete(string id) =>
        _store.TryRemove(id, out _);
}
