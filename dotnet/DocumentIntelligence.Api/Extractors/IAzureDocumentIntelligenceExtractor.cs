namespace DocumentIntelligence.Api.Extractors;

public interface IAzureDocumentIntelligenceExtractor
{
    bool IsConfigured { get; }
    Task<Dictionary<string, object>?> AnalyzeIdentityDocumentAsync(byte[] fileBytes, CancellationToken ct = default);
    Task<Dictionary<string, object>?> AnalyzeInvoiceAsync(byte[] fileBytes, CancellationToken ct = default);
    Task<Dictionary<string, object>?> AnalyzeReceiptAsync(byte[] fileBytes, CancellationToken ct = default);
    Task<Dictionary<string, object>?> AnalyzeGeneralAsync(byte[] fileBytes, CancellationToken ct = default);
}
