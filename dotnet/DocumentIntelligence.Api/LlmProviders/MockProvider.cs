using System.Text.Json;

namespace DocumentIntelligence.Api.LlmProviders;

public class MockProvider : ILlmProvider
{
    private readonly string _filenameHint;

    public string Name => "mock";

    public MockProvider(string? filenameHint = null)
    {
        _filenameHint = (filenameHint ?? "").ToLowerInvariant();
    }

    public Task<Dictionary<string, object>> AnalyzeDocumentAsync(
        string? text = null,
        List<byte[]>? images = null,
        byte[]? rawFileBytes = null,
        string? mimeType = null,
        string? extractionHint = null,
        CancellationToken cancellationToken = default)
    {
        var hint = $"{_filenameHint} {extractionHint ?? ""}".ToLowerInvariant();
        var sample = DetectType(hint) switch
        {
            "identity" => IdentitySample(),
            "payslip" => PayslipSample(),
            "address" => AddressSample(),
            _ => BankSample()
        };
        return Task.FromResult(sample);
    }

    private static string DetectType(string hint)
    {
        if (ContainsAny(hint, "passport", "national_id", " id ", "license", "licence", "permit", "asylum"))
            return "identity";
        if (ContainsAny(hint, "payslip", "salary", "payroll", "wage", "tax_cert"))
            return "payslip";
        if (ContainsAny(hint, "utility", "address", "bill", "municipal", "lease", "insurance", "proof"))
            return "address";
        if (ContainsAny(hint, "statement", "bank", "account"))
            return "bank";
        return "bank";
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(text.Contains);

    private static Dictionary<string, object> BankSample() => Parse("""
        {
          "document_type": "bank_statement",
          "document_subtype": "current_account",
          "title": "[MOCK] FNB Current Account Statement",
          "confidence": 0.97,
          "quality": { "readable": true, "issues": [] },
          "content": {
            "account_holder": "Jane Mock Smith",
            "bank_name": "First National Bank",
            "account_number": "62****4321",
            "branch_code": "250655",
            "statement_period": { "from": "2024-01-01", "to": "2024-01-31" },
            "opening_balance": 15420.50,
            "closing_balance": 18230.75,
            "currency": "ZAR",
            "transactions": [
              { "date": "2024-01-05", "description": "Salary", "type": "credit", "amount": 45000.00, "balance": 60420.50 },
              { "date": "2024-01-07", "description": "Rent", "type": "debit", "amount": 12000.00, "balance": 48420.50 }
            ],
            "total_credits": 45000.00,
            "total_debits": 41689.75
          },
          "validation": { "is_expired": null, "expiry_date": null, "issues": [] }
        }
        """);

    private static Dictionary<string, object> IdentitySample() => Parse("""
        {
          "document_type": "identity_document",
          "document_subtype": "national_id",
          "title": "[MOCK] South African ID Document",
          "confidence": 0.99,
          "quality": { "readable": true, "issues": [] },
          "content": {
            "full_name": "Jane Mock Smith",
            "first_name": "Jane",
            "last_name": "Smith",
            "id_number": "8001015009087",
            "date_of_birth": "1980-01-01",
            "gender": "F",
            "nationality": "South African",
            "country_of_issue": "South Africa",
            "issue_date": "2015-03-10",
            "expiry_date": "2030-03-09",
            "photo_present": true,
            "signature_present": true,
            "document_number": "A12345678",
            "machine_readable_zone": null
          },
          "validation": { "is_expired": false, "expiry_date": "2030-03-09", "issues": [] }
        }
        """);

    private static Dictionary<string, object> PayslipSample() => Parse("""
        {
          "document_type": "payslip",
          "document_subtype": "monthly_payslip",
          "title": "[MOCK] Monthly Payslip - January 2024",
          "confidence": 0.98,
          "quality": { "readable": true, "issues": [] },
          "content": {
            "employee_name": "Jane Mock Smith",
            "employee_id": "EMP-00123",
            "employer_name": "Mock Corp (Pty) Ltd",
            "pay_period": { "from": "2024-01-01", "to": "2024-01-31" },
            "pay_date": "2024-01-25",
            "gross_pay": 55000.00,
            "net_pay": 45000.00,
            "currency": "ZAR",
            "earnings": [{ "description": "Basic Salary", "amount": 50000.00 }],
            "deductions": [{ "description": "PAYE", "amount": 7500.00 }],
            "tax_number": "1234567890",
            "bank_account": "62****4321"
          },
          "validation": { "is_expired": null, "expiry_date": null, "issues": [] }
        }
        """);

    private static Dictionary<string, object> AddressSample() => Parse("""
        {
          "document_type": "proof_of_address",
          "document_subtype": "utility_bill",
          "title": "[MOCK] Electricity Bill - January 2024",
          "confidence": 0.96,
          "quality": { "readable": true, "issues": [] },
          "content": {
            "full_name": "Jane Mock Smith",
            "address": {
              "line1": "123 Mock Street", "line2": null,
              "city": "Cape Town", "state_province": "Western Cape",
              "postal_code": "8001", "country": "South Africa"
            },
            "document_date": "2024-01-15",
            "issuer": "City of Cape Town",
            "account_number": "UTIL-9876543",
            "is_within_3_months": true
          },
          "validation": { "is_expired": null, "expiry_date": null, "issues": [] }
        }
        """);

    private static Dictionary<string, object> Parse(string json)
        => JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
}
