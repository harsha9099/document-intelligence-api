using DocumentIntelligence.Api.LlmProviders;

namespace DocumentIntelligence.Tests;

public class MockProviderTests
{
    [Fact]
    public async Task ReturnsIdentityDocument_WhenFilenameContainsPassport()
    {
        var provider = new MockProvider("passport_scan.pdf");
        var result = await provider.AnalyzeDocumentAsync();
        Assert.Equal("identity_document", result["document_type"]?.ToString());
    }

    [Fact]
    public async Task ReturnsIdentityDocument_WhenFilenameContainsId()
    {
        var provider = new MockProvider("national_id.jpg");
        var result = await provider.AnalyzeDocumentAsync();
        Assert.Equal("identity_document", result["document_type"]?.ToString());
    }

    [Fact]
    public async Task ReturnsBankStatement_WhenFilenameContainsStatement()
    {
        var provider = new MockProvider("bank_statement_jan.pdf");
        var result = await provider.AnalyzeDocumentAsync();
        Assert.Equal("bank_statement", result["document_type"]?.ToString());
    }

    [Fact]
    public async Task ReturnsPayslip_WhenFilenameContainsPayslip()
    {
        var provider = new MockProvider("payslip_march.pdf");
        var result = await provider.AnalyzeDocumentAsync();
        Assert.Equal("payslip", result["document_type"]?.ToString());
    }

    [Fact]
    public async Task ReturnsProofOfAddress_WhenFilenameContainsUtility()
    {
        var provider = new MockProvider("utility_bill.pdf");
        var result = await provider.AnalyzeDocumentAsync();
        Assert.Equal("proof_of_address", result["document_type"]?.ToString());
    }

    [Fact]
    public async Task ReturnsBankStatement_WhenFilenameIsGeneric()
    {
        var provider = new MockProvider("scan.pdf");
        var result = await provider.AnalyzeDocumentAsync();
        Assert.Equal("bank_statement", result["document_type"]?.ToString());
    }

    [Fact]
    public async Task HintOverridesFilenameDetection()
    {
        var provider = new MockProvider("scan.pdf");
        var result = await provider.AnalyzeDocumentAsync(extractionHint: "this is a payslip");
        Assert.Equal("payslip", result["document_type"]?.ToString());
    }

    [Fact]
    public async Task HasRequiredFields()
    {
        var provider = new MockProvider();
        var result = await provider.AnalyzeDocumentAsync();
        Assert.True(result.ContainsKey("document_type"));
        Assert.True(result.ContainsKey("confidence"));
        Assert.True(result.ContainsKey("content"));
        Assert.True(result.ContainsKey("quality"));
        Assert.True(result.ContainsKey("validation"));
    }

    [Fact]
    public void NameIsMock()
    {
        var provider = new MockProvider();
        Assert.Equal("mock", provider.Name);
    }
}
