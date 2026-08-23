import re
from typing import Any

DOCUMENT_PATTERNS: dict[str, dict[str, list[dict[str, Any]]]] = {
    "identity_document": {
        "id_number": [
            {"pattern": r"\b(\d{13})\b", "label": "sa_national_id", "confidence": 0.9},
            {"pattern": r"\b([A-Z]\d{8})\b", "label": "sa_passport", "confidence": 0.85},
            {"pattern": r"\b(\d{2}-\d{7}-\d)\b", "label": "generic_id", "confidence": 0.7},
        ],
        "full_name": [
            {"pattern": r"(?:Name|Naam|Full\s*Name)[:\s]*([A-Z][a-zA-Z\s\-']{2,50})", "confidence": 0.8},
            {"pattern": r"(?:Surname|Last\s*Name)[:\s]*([A-Z][a-zA-Z\-']+)", "field": "last_name", "confidence": 0.8},
            {"pattern": r"(?:First\s*Name|Given\s*Name)[:\s]*([A-Z][a-zA-Z\-']+)", "field": "first_name", "confidence": 0.8},
        ],
        "date_of_birth": [
            {"pattern": r"(?:Date\s*of\s*Birth|DOB|Geboortedatum)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", "confidence": 0.85},
            {"pattern": r"(?:Date\s*of\s*Birth|DOB|Geboortedatum)[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", "confidence": 0.85},
            {"pattern": r"(?:Born)[:\s]*(\d{2}\s\w+\s\d{4})", "confidence": 0.75},
        ],
        "gender": [
            {"pattern": r"(?:Sex|Gender|Geslag)[:\s]*(Male|Female|M|F)", "confidence": 0.9},
        ],
        "nationality": [
            {"pattern": r"(?:Nationality|Nasionaliteit|Citizenship)[:\s]*([A-Za-z\s]+?)(?:\n|$)", "confidence": 0.8},
        ],
        "expiry_date": [
            {"pattern": r"(?:Expiry|Expir|Valid\s*Until|Geldig\s*Tot)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", "confidence": 0.85},
            {"pattern": r"(?:Expiry|Expir|Valid\s*Until)[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", "confidence": 0.85},
        ],
    },
    "bank_statement": {
        "account_number": [
            {"pattern": r"(?:Account\s*(?:No|Number|#))[:\s]*([\d\s\-]{8,20})", "confidence": 0.9},
            {"pattern": r"(?:Acc\s*No)[:\s]*([\d\-]{8,15})", "confidence": 0.85},
        ],
        "bank_name": [
            {"pattern": r"(FNB|First National Bank|ABSA|Standard Bank|Nedbank|Capitec|Investec|African Bank|TymeBank)", "confidence": 0.95},
            {"pattern": r"(HSBC|Barclays|Lloyds|NatWest|Santander|Chase|Bank of America|Wells Fargo)", "confidence": 0.90},
        ],
        "statement_period": [
            {"pattern": r"(?:Statement\s*Period|Period)[:\s]*(.+?)(?:\n|$)", "confidence": 0.8},
            {"pattern": r"(\d{1,2}\s\w+\s\d{4})\s*(?:to|-)\s*(\d{1,2}\s\w+\s\d{4})", "confidence": 0.75},
        ],
        "opening_balance": [
            {"pattern": r"(?:Opening|Previous|Beginning)\s*Balance[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", "confidence": 0.85},
        ],
        "closing_balance": [
            {"pattern": r"(?:Closing|Final|Ending)\s*Balance[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", "confidence": 0.85},
        ],
    },
    "proof_of_address": {
        "full_name": [
            {"pattern": r"(?:Name|Customer|Account\s*Holder|Tenant)[:\s]*([A-Z][a-zA-Z\s\-']{2,50})", "confidence": 0.8},
        ],
        "address": [
            {"pattern": r"(?:Address|Postal\s*Address|Physical\s*Address|Service\s*Address)[:\s]*(.+?)(?:\n\n|\n[A-Z])", "confidence": 0.7, "multiline": True},
            {"pattern": r"(\d+\s+[A-Za-z\s]+(?:Street|St|Road|Rd|Avenue|Ave|Drive|Dr|Crescent|Cres|Lane|Ln|Boulevard|Blvd)[,.\s]+.+?\d{4,5})", "confidence": 0.75},
        ],
        "issue_date": [
            {"pattern": r"(?:Date|Statement\s*Date|Invoice\s*Date|Bill\s*Date)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", "confidence": 0.8},
            {"pattern": r"(?:Date|Statement\s*Date|Invoice\s*Date)[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", "confidence": 0.8},
            {"pattern": r"(?:Date)[:\s]*(\d{1,2}\s\w+\s\d{4})", "confidence": 0.75},
        ],
    },
    "payslip": {
        "employee_name": [
            {"pattern": r"(?:Employee|Name|Werknemer)[:\s]*([A-Z][a-zA-Z\s\-']{2,50})", "confidence": 0.8},
        ],
        "employer_name": [
            {"pattern": r"(?:Employer|Company|Werkgewer)[:\s]*([A-Za-z][a-zA-Z\s\-&.]{2,60})", "confidence": 0.8},
        ],
        "gross_pay": [
            {"pattern": r"(?:Gross\s*Pay|Gross\s*Salary|Gross\s*Earnings|Total\s*Earnings)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", "confidence": 0.85},
        ],
        "net_pay": [
            {"pattern": r"(?:Net\s*Pay|Net\s*Salary|Take\s*Home|Nett\s*Pay)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", "confidence": 0.85},
        ],
        "tax_deducted": [
            {"pattern": r"(?:PAYE|Tax|Income\s*Tax)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", "confidence": 0.8},
        ],
        "pay_period": [
            {"pattern": r"(?:Pay\s*Period|Period|Month)[:\s]*(.+?)(?:\n|$)", "confidence": 0.75},
        ],
    },
    "invoice": {
        "invoice_number": [
            {"pattern": r"(?:Invoice\s*(?:No|Number|#|Ref))[:\s]*([A-Z0-9\-/]+)", "confidence": 0.9},
            {"pattern": r"(?:INV)[- ]?(\d{3,10})", "confidence": 0.85},
        ],
        "vendor_name": [
            {"pattern": r"(?:From|Vendor|Supplier|Issued\s*by|Company)[:\s]*([A-Za-z][a-zA-Z\s\-&.]{2,60})", "confidence": 0.7},
        ],
        "total_amount": [
            {"pattern": r"(?:Total\s*(?:Due|Amount|Payable)|Grand\s*Total|Amount\s*Due)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", "confidence": 0.9},
            {"pattern": r"(?:Total)[:\s]*[R$£€]\s*([\d,]+\.\d{2})", "confidence": 0.8},
        ],
        "invoice_date": [
            {"pattern": r"(?:Invoice\s*Date|Date\s*of\s*Invoice|Date)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", "confidence": 0.85},
            {"pattern": r"(?:Invoice\s*Date|Date)[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", "confidence": 0.85},
            {"pattern": r"(?:Date)[:\s]*(\d{1,2}\s\w+\s\d{4})", "confidence": 0.75},
        ],
        "due_date": [
            {"pattern": r"(?:Due\s*Date|Payment\s*Due|Pay\s*by)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", "confidence": 0.85},
            {"pattern": r"(?:Due\s*Date|Payment\s*Due)[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", "confidence": 0.85},
        ],
        "currency": [
            {"pattern": r"(ZAR|USD|GBP|EUR|R\s)", "confidence": 0.85},
        ],
    },
    "bill": {
        "provider_name": [
            {"pattern": r"(Eskom|City\s*(?:of|Power)|Rand\s*Water|Telkom|MTN|Vodacom|Cell\s*C|Multichoice|DStv)", "confidence": 0.95},
            {"pattern": r"(Verizon|AT&T|Comcast|British\s*Gas|EDF|Thames\s*Water)", "confidence": 0.90},
            {"pattern": r"(?:From|Provider|Service\s*Provider)[:\s]*([A-Za-z][a-zA-Z\s\-&.]{2,40})", "confidence": 0.7},
        ],
        "account_number": [
            {"pattern": r"(?:Account\s*(?:No|Number|#)|Customer\s*(?:No|Number))[:\s]*([\w\-]{5,20})", "confidence": 0.85},
        ],
        "total_due": [
            {"pattern": r"(?:Total\s*(?:Due|Amount|Payable)|Amount\s*Due|Balance\s*Due)[:\s]*[R$£€]?\s*([\d,]+\.\d{2})", "confidence": 0.9},
        ],
        "bill_date": [
            {"pattern": r"(?:Bill\s*Date|Statement\s*Date|Date)[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", "confidence": 0.85},
            {"pattern": r"(?:Bill\s*Date|Statement\s*Date|Date)[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", "confidence": 0.85},
        ],
        "due_date": [
            {"pattern": r"(?:Due\s*Date|Pay\s*(?:by|before))[:\s]*(\d{4}[/-]\d{2}[/-]\d{2})", "confidence": 0.85},
            {"pattern": r"(?:Due\s*Date|Pay\s*(?:by|before))[:\s]*(\d{2}[/-]\d{2}[/-]\d{4})", "confidence": 0.85},
        ],
    },
}

TYPE_DETECTION_PATTERNS: list[dict[str, Any]] = [
    {"pattern": r"(?:Identity|ID)\s*(?:Document|Card|Book)|Passport|Driver.?s?\s*Licen[cs]e", "type": "identity_document", "confidence": 0.8},
    {"pattern": r"(?:Bank\s*Statement|Account\s*Statement|Transaction\s*History)", "type": "bank_statement", "confidence": 0.85},
    {"pattern": r"(?:Invoice|Tax\s*Invoice|Proforma)", "type": "invoice", "confidence": 0.9},
    {"pattern": r"(?:Payslip|Pay\s*Slip|Salary\s*Advice|Remuneration)", "type": "payslip", "confidence": 0.85},
    {"pattern": r"(?:Utility|Electricity|Water|Gas)\s*(?:Bill|Account|Statement)", "type": "bill", "confidence": 0.85},
    {"pattern": r"(?:Lease\s*Agreement|Municipal|Rates\s*and\s*Taxes)", "type": "proof_of_address", "confidence": 0.8},
    {"pattern": r"(?:Total\s*Due|Amount\s*Due|Pay\s*by)", "type": "bill", "confidence": 0.6},
]


class PatternExtractionResult:
    def __init__(self):
        self.fields: dict[str, Any] = {}
        self.field_confidences: dict[str, float] = {}
        self.detected_type: str | None = None
        self.type_confidence: float = 0.0
        self.patterns_matched: int = 0
        self.patterns_attempted: int = 0
        self.matched_pattern_ids: list[str] = []

    @property
    def overall_confidence(self) -> float:
        if not self.field_confidences:
            return 0.0
        return round(sum(self.field_confidences.values()) / len(self.field_confidences), 3)

    @property
    def extraction_rate(self) -> float:
        if self.patterns_attempted == 0:
            return 0.0
        return self.patterns_matched / self.patterns_attempted


def detect_document_type(text: str) -> tuple[str | None, float]:
    for entry in TYPE_DETECTION_PATTERNS:
        if re.search(entry["pattern"], text, re.IGNORECASE):
            return entry["type"], entry["confidence"]
    return None, 0.0


def extract_with_patterns(
    text: str,
    document_type: str | None = None,
    stored_patterns: list | None = None,
) -> PatternExtractionResult:
    result = PatternExtractionResult()

    if not text or len(text.strip()) < 20:
        return result

    if document_type is None or document_type == "auto":
        detected_type, type_conf = detect_document_type(text)
        result.detected_type = detected_type
        result.type_confidence = type_conf
        document_type = detected_type

    if document_type is None:
        return result

    # Use stored patterns from DB if provided, otherwise fall back to hardcoded
    if stored_patterns:
        result.matched_pattern_ids = []
        fields_grouped: dict[str, list] = {}
        for sp in stored_patterns:
            fields_grouped.setdefault(sp.field_name, []).append(sp)

        result.patterns_attempted = len(fields_grouped)
        for field_name, field_pats in fields_grouped.items():
            for sp in sorted(field_pats, key=lambda p: p.confidence, reverse=True):
                try:
                    match = re.search(sp.pattern, text, re.IGNORECASE)
                    if match:
                        value = match.group(1).strip() if match.lastindex else match.group(0).strip()
                        result.fields[field_name] = value
                        result.field_confidences[field_name] = sp.confidence
                        result.patterns_matched += 1
                        result.matched_pattern_ids.append(sp.id)
                        break
                except re.error:
                    continue
    else:
        result.matched_pattern_ids = []
        patterns = DOCUMENT_PATTERNS.get(document_type, {})
        result.patterns_attempted = len(patterns)

        for field_name, field_patterns in patterns.items():
            for pat_def in field_patterns:
                flags = re.IGNORECASE | (re.DOTALL if pat_def.get("multiline") else 0)
                match = re.search(pat_def["pattern"], text, flags)
                if match:
                    value = match.group(1).strip() if match.lastindex else match.group(0).strip()
                    actual_field = pat_def.get("field", field_name)
                    result.fields[actual_field] = value
                    result.field_confidences[actual_field] = pat_def["confidence"]
                    result.patterns_matched += 1
                    break

    return result
