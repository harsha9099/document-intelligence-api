namespace DocumentIntelligence.Api.Services;

public static class FieldRequirements
{
    private static readonly Dictionary<string, string[]> Required = new()
    {
        ["identity_document"] = ["id_number", "full_name", "date_of_birth"],
        ["bank_statement"]    = ["account_number", "bank_name", "transactions"],
        ["proof_of_address"]  = ["full_name", "address"],
        ["payslip"]           = ["employee_name", "gross_pay", "net_pay"],
        ["invoice"]           = ["invoice_number", "total_amount", "vendor_name"],
        ["bill"]              = ["total_due", "provider_name"],
    };

    public static (bool IsComplete, List<string> Missing) Check(
        string documentType, Dictionary<string, object> content)
    {
        if (!Required.TryGetValue(documentType, out var fields))
            return (true, []);

        var missing = fields
            .Where(f => !content.TryGetValue(f, out var v) || v is null || v.ToString() == "")
            .ToList();

        return (missing.Count == 0, missing);
    }
}
