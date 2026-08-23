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

    public IReadOnlyList<DocumentResponse> ListAll() =>
        _store.Values.ToList().AsReadOnly();
}
