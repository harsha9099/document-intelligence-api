using DocumentIntelligence.Api.Models;

namespace DocumentIntelligence.Api.Repositories;

public interface IDocumentRepository
{
    void Save(DocumentResponse document);
    DocumentResponse? Get(string id);
    IReadOnlyList<DocumentResponse> ListAll();
}
