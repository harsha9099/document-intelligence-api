from typing import Any

REQUIRED_FIELDS: dict[str, list[str]] = {
    "identity_document": ["id_number", "full_name", "date_of_birth"],
    "bank_statement": ["account_number", "bank_name", "transactions"],
    "proof_of_address": ["full_name", "address"],
    "payslip": ["employee_name", "gross_pay", "net_pay"],
    "invoice": ["invoice_number", "total_amount", "vendor_name"],
    "bill": ["total_due", "provider_name"],
}


def check_field_completeness(document_type: str, content: dict[str, Any]) -> tuple[bool, list[str]]:
    required = REQUIRED_FIELDS.get(document_type, [])
    if not required:
        return True, []

    missing = [
        f for f in required
        if not content.get(f) and content.get(f) != 0
    ]
    return len(missing) == 0, missing
