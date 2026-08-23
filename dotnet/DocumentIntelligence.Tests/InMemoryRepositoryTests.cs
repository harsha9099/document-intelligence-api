using DocumentIntelligence.Api.Models;
using DocumentIntelligence.Api.Repositories;

namespace DocumentIntelligence.Tests;

public class InMemoryRepositoryTests
{
    private static DocumentResponse MakeDoc(string type = "bank_statement") => new()
    {
        DocumentType = type,
        Title = "Test",
        Confidence = 0.9,
        Content = []
    };

    [Fact]
    public void Save_StoresDocument()
    {
        var repo = new InMemoryDocumentRepository();
        var doc = MakeDoc();
        repo.Save(doc);
        Assert.NotNull(repo.Get(doc.Id));
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownId()
    {
        var repo = new InMemoryDocumentRepository();
        Assert.Null(repo.Get("nonexistent"));
    }

    [Fact]
    public void ListAll_ReturnsEmpty_WhenNothingSaved()
    {
        var repo = new InMemoryDocumentRepository();
        Assert.Empty(repo.ListAll());
    }

    [Fact]
    public void ListAll_ReturnsAllSaved()
    {
        var repo = new InMemoryDocumentRepository();
        repo.Save(MakeDoc("payslip"));
        repo.Save(MakeDoc("bank_statement"));
        var all = repo.ListAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void EachDocument_HasUniqueId()
    {
        var repo = new InMemoryDocumentRepository();
        var a = MakeDoc();
        var b = MakeDoc();
        repo.Save(a);
        repo.Save(b);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Get_ReturnsCorrectDocument()
    {
        var repo = new InMemoryDocumentRepository();
        var doc = MakeDoc("identity_document");
        repo.Save(doc);
        var retrieved = repo.Get(doc.Id);
        Assert.Equal("identity_document", retrieved?.DocumentType);
    }

    [Fact]
    public void Save_Overwrites_SameId()
    {
        var repo = new InMemoryDocumentRepository();
        var doc = MakeDoc("payslip");
        repo.Save(doc);
        // Save updated version with same ID
        var updated = doc with { Title = "Updated" };
        repo.Save(updated);
        Assert.Equal("Updated", repo.Get(doc.Id)?.Title);
        Assert.Single(repo.ListAll());
    }
}
