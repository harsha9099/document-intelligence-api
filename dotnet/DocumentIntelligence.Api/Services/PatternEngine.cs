using System.Text.RegularExpressions;

namespace DocumentIntelligence.Api.Services;

public class PatternExtractionResult
{
    public Dictionary<string, object> Fields { get; } = new();
    public Dictionary<string, double> FieldConfidences { get; } = new();
    public string? DetectedType { get; set; }
    public double TypeConfidence { get; set; }
    public int PatternsMatched { get; set; }
    public int PatternsAttempted { get; set; }

    public double OverallConfidence =>
        FieldConfidences.Count == 0 ? 0.0 : Math.Round(FieldConfidences.Values.Average(), 3);
}

public static class PatternEngine
{
    private static readonly Dictionary<string, Dictionary<string, List<PatternDef>>> DocumentPatterns = new()
    {
        ["identity_document"] = new()
        {
            ["id_number"] = [
                new(@"\b(\d{13})\b", 0.9),
                new(@"\b([A-Z]\d{8})\b", 0.85),
            ],
            ["full_name"] = [
                new(@"(?:Name|Naam|Full\s*Name)[:\s]*([A-Z][a-zA-Z\s\-']{2,50})", 0.8),
            ],
            ["date_of_birth"] = [
                new(@"(?:Date\s*of\s*Birth|DOB|Geboortedatum)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", 0.85),
                new(@"(?:Date\s*of\s*Birth|DOB)[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", 0.85),
            ],
            ["gender"] = [
                new(@"(?:Sex|Gender|Geslag)[:\s]*(Male|Female|M|F)", 0.9),
            ],
            ["expiry_date"] = [
                new(@"(?:Expiry|Valid\s*Until)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", 0.85),
            ],
        },
        ["bank_statement"] = new()
        {
            ["account_number"] = [
                new(@"(?:Account\s*(?:No|Number|#))[:\s]*([\d\s\-]{8,20})", 0.9),
            ],
            ["bank_name"] = [
                new(@"(FNB|First National Bank|ABSA|Standard Bank|Nedbank|Capitec|Investec|African Bank|TymeBank)", 0.95),
                new(@"(HSBC|Barclays|Lloyds|NatWest|Santander|Chase|Bank of America|Wells Fargo)", 0.90),
            ],
            ["transactions"] = [
                new(@"(Debit|Credit|Payment|Transfer|Deposit).+?([\d,]+\.\d{2})", 0.7),
            ],
            ["opening_balance"] = [
                new(@"(?:Opening|Previous|Beginning)\s*Balance[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", 0.85),
            ],
            ["closing_balance"] = [
                new(@"(?:Closing|Final|Ending)\s*Balance[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", 0.85),
            ],
        },
        ["proof_of_address"] = new()
        {
            ["full_name"] = [
                new(@"(?:Name|Customer|Account\s*Holder|Tenant)[:\s]*([A-Z][a-zA-Z\s\-']{2,50})", 0.8),
            ],
            ["address"] = [
                new(@"(?:Address|Postal\s*Address|Physical\s*Address|Service\s*Address)[:\s]*(.+?)(?:\n\n|\n[A-Z])", 0.7),
                new(@"(\d+\s+[A-Za-z\s]+(?:Street|St|Road|Rd|Avenue|Ave|Drive|Dr)\s*[,.\s]+.+?\d{4,5})", 0.75),
            ],
        },
        ["payslip"] = new()
        {
            ["employee_name"] = [
                new(@"(?:Employee|Name|Werknemer)[:\s]*([A-Z][a-zA-Z\s\-']{2,50})", 0.8),
            ],
            ["gross_pay"] = [
                new(@"(?:Gross\s*Pay|Gross\s*Salary|Total\s*Earnings)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", 0.85),
            ],
            ["net_pay"] = [
                new(@"(?:Net\s*Pay|Net\s*Salary|Take\s*Home|Nett\s*Pay)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", 0.85),
            ],
        },
        ["invoice"] = new()
        {
            ["invoice_number"] = [
                new(@"(?:Invoice\s*(?:No|Number|#|Ref))[:\s]*([A-Z0-9\-/]+)", 0.9),
                new(@"(?:INV)[- ]?(\d{3,10})", 0.85),
            ],
            ["vendor_name"] = [
                new(@"(?:From|Vendor|Supplier|Issued\s*by)[:\s]*([A-Za-z][a-zA-Z\s\-&.]{2,60})", 0.7),
            ],
            ["total_amount"] = [
                new(@"(?:Total\s*(?:Due|Amount|Payable)|Grand\s*Total)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", 0.9),
            ],
        },
        ["bill"] = new()
        {
            ["provider_name"] = [
                new(@"(Eskom|City\s*(?:of|Power)|Rand\s*Water|Telkom|MTN|Vodacom|Cell\s*C|Multichoice|DStv)", 0.95),
                new(@"(?:From|Provider|Service\s*Provider)[:\s]*([A-Za-z][a-zA-Z\s\-&.]{2,40})", 0.7),
            ],
            ["total_due"] = [
                new(@"(?:Total\s*(?:Due|Amount|Payable)|Amount\s*Due)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", 0.9),
            ],
        },
    };

    private static readonly List<(string Pattern, string Type, double Confidence)> TypeDetectionPatterns =
    [
        (@"(?:Identity|ID)\s*(?:Document|Card|Book)|Passport|Driver.?s?\s*Licen[cs]e", "identity_document", 0.8),
        (@"(?:Bank\s*Statement|Account\s*Statement|Transaction\s*History)", "bank_statement", 0.85),
        (@"(?:Invoice|Tax\s*Invoice|Proforma)", "invoice", 0.9),
        (@"(?:Payslip|Pay\s*Slip|Salary\s*Advice|Remuneration)", "payslip", 0.85),
        (@"(?:Utility|Electricity|Water|Gas)\s*(?:Bill|Account|Statement)", "bill", 0.85),
        (@"(?:Lease\s*Agreement|Municipal|Rates\s*and\s*Taxes)", "proof_of_address", 0.8),
    ];

    public static (string? Type, double Confidence) DetectDocumentType(string text)
    {
        foreach (var (pattern, type, confidence) in TypeDetectionPatterns)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                return (type, confidence);
        }
        return (null, 0.0);
    }

    public static PatternExtractionResult Extract(string text, string? documentType = null)
    {
        var result = new PatternExtractionResult();

        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 20)
            return result;

        if (string.IsNullOrEmpty(documentType) || documentType == "auto")
        {
            var (detected, conf) = DetectDocumentType(text);
            result.DetectedType = detected;
            result.TypeConfidence = conf;
            documentType = detected;
        }

        if (documentType is null || !DocumentPatterns.TryGetValue(documentType, out var patterns))
            return result;

        result.PatternsAttempted = patterns.Count;

        foreach (var (fieldName, fieldPatterns) in patterns)
        {
            foreach (var pat in fieldPatterns)
            {
                var match = Regex.Match(text, pat.Pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var value = match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : match.Value.Trim();
                    result.Fields[fieldName] = value;
                    result.FieldConfidences[fieldName] = pat.Confidence;
                    result.PatternsMatched++;
                    break;
                }
            }
        }

        return result;
    }

    private record PatternDef(string Pattern, double Confidence);
}
