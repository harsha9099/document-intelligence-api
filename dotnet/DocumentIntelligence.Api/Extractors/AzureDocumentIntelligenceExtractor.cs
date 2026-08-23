using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;

namespace DocumentIntelligence.Api.Extractors;

public class AzureDocumentIntelligenceExtractor : IAzureDocumentIntelligenceExtractor
{
    private readonly DocumentAnalysisClient? _client;

    public bool IsConfigured => _client is not null;

    public AzureDocumentIntelligenceExtractor(IConfiguration config)
    {
        var endpoint = config["Extraction:AzureDiEndpoint"];
        var key = config["Extraction:AzureDiKey"];
        if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(key))
            _client = new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));
    }

    public async Task<Dictionary<string, object>?> AnalyzeIdentityDocumentAsync(byte[] fileBytes, CancellationToken ct = default)
    {
        if (_client is null) return null;
        try
        {
            using var stream = new MemoryStream(fileBytes);
            var op = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-idDocument", stream, cancellationToken: ct);
            var result = op.Value;
            if (result.Documents.Count == 0) return null;

            var doc = result.Documents[0];
            var f = doc.Fields;
            var confidence = AverageConfidence(f);
            var expiry = GetFieldString(f, "DateOfExpiration");
            var dob = GetFieldString(f, "DateOfBirth");
            var firstName = GetFieldString(f, "FirstName");
            var lastName = GetFieldString(f, "LastName");

            return new Dictionary<string, object>
            {
                ["document_type"] = "identity_document",
                ["document_subtype"] = doc.DocumentType?.Replace("idDocument.", "") ?? "national_id",
                ["title"] = "Identity Document (Azure DI)",
                ["confidence"] = confidence,
                ["quality"] = new Dictionary<string, object> { ["readable"] = true, ["issues"] = Array.Empty<string>() },
                ["content"] = new Dictionary<string, object?>
                {
                    ["full_name"] = string.IsNullOrEmpty(firstName) ? null : $"{firstName} {lastName}".Trim(),
                    ["first_name"] = firstName,
                    ["last_name"] = lastName,
                    ["id_number"] = GetFieldString(f, "DocumentNumber"),
                    ["date_of_birth"] = TruncateDate(dob),
                    ["gender"] = GetFieldString(f, "Sex"),
                    ["nationality"] = GetFieldString(f, "Nationality"),
                    ["country_of_issue"] = GetFieldString(f, "CountryRegion"),
                    ["expiry_date"] = TruncateDate(expiry),
                    ["address"] = GetFieldString(f, "Address"),
                    ["photo_present"] = true,
                    ["signature_present"] = false,
                    ["document_number"] = GetFieldString(f, "DocumentNumber"),
                    ["machine_readable_zone"] = GetFieldString(f, "MachineReadableZone"),
                },
                ["validation"] = new Dictionary<string, object?> { ["is_expired"] = false, ["expiry_date"] = TruncateDate(expiry), ["issues"] = Array.Empty<string>() },
            };
        }
        catch { return null; }
    }

    public async Task<Dictionary<string, object>?> AnalyzeInvoiceAsync(byte[] fileBytes, CancellationToken ct = default)
    {
        if (_client is null) return null;
        try
        {
            using var stream = new MemoryStream(fileBytes);
            var op = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-invoice", stream, cancellationToken: ct);
            var result = op.Value;
            if (result.Documents.Count == 0) return null;

            var doc = result.Documents[0];
            var f = doc.Fields;
            var confidence = AverageConfidence(f);
            var invoiceDate = GetFieldString(f, "InvoiceDate");
            var dueDate = GetFieldString(f, "DueDate");

            return new Dictionary<string, object>
            {
                ["document_type"] = "invoice",
                ["document_subtype"] = "tax_invoice",
                ["title"] = $"Invoice {GetFieldString(f, "InvoiceId") ?? ""} (Azure DI)".Trim(),
                ["confidence"] = confidence,
                ["quality"] = new Dictionary<string, object> { ["readable"] = true, ["issues"] = Array.Empty<string>() },
                ["content"] = new Dictionary<string, object?>
                {
                    ["vendor_name"] = GetFieldString(f, "VendorName"),
                    ["vendor_address"] = GetFieldString(f, "VendorAddress"),
                    ["customer_name"] = GetFieldString(f, "CustomerName"),
                    ["customer_address"] = GetFieldString(f, "CustomerAddress"),
                    ["invoice_number"] = GetFieldString(f, "InvoiceId"),
                    ["invoice_date"] = TruncateDate(invoiceDate),
                    ["due_date"] = TruncateDate(dueDate),
                    ["purchase_order_number"] = GetFieldString(f, "PurchaseOrder"),
                    ["line_items"] = new List<object>(),
                    ["subtotal"] = GetFieldDouble(f, "SubTotal"),
                    ["tax_amount"] = GetFieldDouble(f, "TotalTax"),
                    ["tax_rate"] = (object?)null,
                    ["total_amount"] = GetFieldDouble(f, "InvoiceTotal"),
                    ["currency"] = (object?)null,
                    ["payment_terms"] = (object?)null,
                    ["bank_details"] = (object?)null,
                },
                ["validation"] = new Dictionary<string, object?> { ["is_expired"] = null, ["expiry_date"] = null, ["issues"] = Array.Empty<string>() },
            };
        }
        catch { return null; }
    }

    public async Task<Dictionary<string, object>?> AnalyzeReceiptAsync(byte[] fileBytes, CancellationToken ct = default)
    {
        if (_client is null) return null;
        try
        {
            using var stream = new MemoryStream(fileBytes);
            var op = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-receipt", stream, cancellationToken: ct);
            var result = op.Value;
            if (result.Documents.Count == 0) return null;

            var doc = result.Documents[0];
            var f = doc.Fields;
            var txDate = TruncateDate(GetFieldString(f, "TransactionDate"));

            return new Dictionary<string, object>
            {
                ["document_type"] = "bill",
                ["document_subtype"] = "other_bill",
                ["title"] = $"Receipt from {GetFieldString(f, "MerchantName") ?? "unknown"} (Azure DI)",
                ["confidence"] = AverageConfidence(f),
                ["quality"] = new Dictionary<string, object> { ["readable"] = true, ["issues"] = Array.Empty<string>() },
                ["content"] = new Dictionary<string, object?>
                {
                    ["account_holder"] = null, ["provider_name"] = GetFieldString(f, "MerchantName"),
                    ["account_number"] = null, ["billing_period"] = null, ["bill_date"] = txDate,
                    ["due_date"] = null, ["previous_balance"] = null, ["payments_received"] = null,
                    ["current_charges"] = GetFieldDouble(f, "Subtotal"),
                    ["total_due"] = GetFieldDouble(f, "Total"),
                    ["currency"] = null, ["line_items"] = new List<object>(),
                },
                ["validation"] = new Dictionary<string, object?> { ["is_expired"] = null, ["expiry_date"] = null, ["issues"] = Array.Empty<string>() },
            };
        }
        catch { return null; }
    }

    public async Task<Dictionary<string, object>?> AnalyzeReadAsync(byte[] fileBytes, CancellationToken ct = default)
    {
        if (_client is null) return null;
        try
        {
            using var stream = new MemoryStream(fileBytes);
            var op = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", stream, cancellationToken: ct);
            var result = op.Value;
            var text = result.Content ?? "";
            if (string.IsNullOrWhiteSpace(text)) return null;
            return new Dictionary<string, object>
            {
                ["document_type"] = "unknown", ["document_subtype"] = null!,
                ["title"] = "Document (Azure DI Read)", ["confidence"] = 0.7,
                ["quality"] = new Dictionary<string, object> { ["readable"] = true, ["issues"] = Array.Empty<string>() },
                ["content"] = new Dictionary<string, object> { ["raw_text"] = text[..Math.Min(3000, text.Length)] },
                ["validation"] = new Dictionary<string, object?> { ["is_expired"] = null, ["expiry_date"] = null, ["issues"] = Array.Empty<string>() },
            };
        }
        catch { return null; }
    }

    public async Task<Dictionary<string, object>?> AnalyzeGeneralAsync(byte[] fileBytes, CancellationToken ct = default)
    {
        if (_client is null) return null;
        try
        {
            using var stream = new MemoryStream(fileBytes);
            var op = await _client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-layout", stream, cancellationToken: ct);
            var result = op.Value;
            var text = string.Join("\n", result.Paragraphs.Select(p => p.Content));
            if (string.IsNullOrWhiteSpace(text)) return null;
            return new Dictionary<string, object>
            {
                ["document_type"] = "unknown", ["document_subtype"] = null!,
                ["title"] = "Document (Azure DI Layout)", ["confidence"] = 0.5,
                ["quality"] = new Dictionary<string, object> { ["readable"] = true, ["issues"] = Array.Empty<string>() },
                ["content"] = new Dictionary<string, object> { ["raw_layout_text"] = text[..Math.Min(2000, text.Length)] },
                ["validation"] = new Dictionary<string, object?> { ["is_expired"] = null, ["expiry_date"] = null, ["issues"] = Array.Empty<string>() },
            };
        }
        catch { return null; }
    }

    private static double AverageConfidence(IReadOnlyDictionary<string, DocumentField> fields)
    {
        var vals = fields.Values.Select(f => f.Confidence).Where(c => c.HasValue).Select(c => c!.Value).ToList();
        return vals.Count > 0 ? Math.Round(vals.Average(), 3) : 0.75;
    }

    private static string? GetFieldString(IReadOnlyDictionary<string, DocumentField> f, string key)
        => f.TryGetValue(key, out var field) ? field.Content : null;

    private static double? GetFieldDouble(IReadOnlyDictionary<string, DocumentField> f, string key)
    {
        if (!f.TryGetValue(key, out var field)) return null;
        if (field.FieldType == DocumentFieldType.Double) return field.Value.AsDouble();
        if (double.TryParse(field.Content, out var d)) return d;
        return null;
    }

    private static string? TruncateDate(string? value) => value?.Length >= 10 ? value[..10] : value;
}
