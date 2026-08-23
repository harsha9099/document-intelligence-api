import logging
from typing import Any

from app.llm.base import LLMProvider

logger = logging.getLogger(__name__)


def _detect_type(filename: str) -> str:
    name = (filename or "").lower()
    if any(k in name for k in ("passport", "identity", "national_id", "license", "licence", "permit", "asylum")):
        return "identity"
    if any(k in name for k in ("statement", "bank", "account")):
        return "bank"
    if any(k in name for k in ("payslip", "salary", "pay", "wage", "tax_cert")):
        return "payslip"
    if any(k in name for k in ("invoice", "proforma", "commercial_invoice", "tax_invoice")):
        return "invoice"
    if any(k in name for k in ("phone_bill", "medical_bill", "subscription")):
        return "bill"
    if any(k in name for k in ("utility", "address", "municipal", "lease", "insurance", "proof")):
        return "address"
    return "bank"


_SAMPLES: dict[str, dict[str, Any]] = {
    "identity": {
        "document_type": "identity_document",
        "document_subtype": "national_id",
        "title": "[MOCK] South African ID Document",
        "confidence": 0.99,
        "quality": {"readable": True, "issues": []},
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
            "address": None,
            "photo_present": True,
            "signature_present": True,
            "document_number": "A12345678",
            "machine_readable_zone": None,
        },
        "validation": {"is_expired": False, "expiry_date": "2030-03-09", "issues": []},
    },
    "bank": {
        "document_type": "bank_statement",
        "document_subtype": "current_account",
        "title": "[MOCK] FNB Current Account Statement",
        "confidence": 0.97,
        "quality": {"readable": True, "issues": []},
        "content": {
            "account_holder": "Jane Mock Smith",
            "bank_name": "First National Bank",
            "account_number": "62****4321",
            "branch_code": "250655",
            "statement_period": {"from": "2024-01-01", "to": "2024-01-31"},
            "opening_balance": 15420.50,
            "closing_balance": 18230.75,
            "currency": "ZAR",
            "address": {
                "line1": "123 Mock Street",
                "line2": None,
                "city": "Cape Town",
                "state_province": "Western Cape",
                "postal_code": "8001",
                "country": "South Africa",
            },
            "transactions": [
                {"date": "2024-01-05", "description": "Salary Deposit", "type": "credit", "amount": 45000.00, "balance": 60420.50},
                {"date": "2024-01-07", "description": "Rent Payment", "type": "debit", "amount": 12000.00, "balance": 48420.50},
                {"date": "2024-01-15", "description": "Groceries", "type": "debit", "amount": 2300.25, "balance": 46120.25},
            ],
            "total_credits": 45000.00,
            "total_debits": 41689.75,
        },
        "validation": {"is_expired": None, "expiry_date": None, "issues": []},
    },
    "payslip": {
        "document_type": "payslip",
        "document_subtype": "monthly_payslip",
        "title": "[MOCK] Monthly Payslip - January 2024",
        "confidence": 0.98,
        "quality": {"readable": True, "issues": []},
        "content": {
            "employee_name": "Jane Mock Smith",
            "employee_id": "EMP-00123",
            "employer_name": "Mock Corp (Pty) Ltd",
            "employer_address": "1 Business Park, Cape Town, 8001",
            "pay_period": {"from": "2024-01-01", "to": "2024-01-31"},
            "pay_date": "2024-01-25",
            "gross_pay": 55000.00,
            "net_pay": 45000.00,
            "currency": "ZAR",
            "earnings": [
                {"description": "Basic Salary", "amount": 50000.00},
                {"description": "Travel Allowance", "amount": 5000.00},
            ],
            "deductions": [
                {"description": "PAYE", "amount": 7500.00},
                {"description": "UIF", "amount": 500.00},
                {"description": "Medical Aid", "amount": 2000.00},
            ],
            "tax_number": "1234567890",
            "bank_account": "62****4321",
        },
        "validation": {"is_expired": None, "expiry_date": None, "issues": []},
    },
    "invoice": {
        "document_type": "invoice",
        "document_subtype": "tax_invoice",
        "title": "[MOCK] Tax Invoice INV-2024-00123",
        "confidence": 0.97,
        "quality": {"readable": True, "issues": []},
        "content": {
            "vendor_name": "Mock Supplies (Pty) Ltd",
            "vendor_address": "99 Trade Park, Johannesburg, 2001",
            "customer_name": "Jane Mock Smith",
            "customer_address": "123 Mock Street, Cape Town, 8001",
            "invoice_number": "INV-2024-00123",
            "invoice_date": "2024-01-15",
            "due_date": "2024-02-15",
            "purchase_order_number": "PO-9876",
            "line_items": [
                {"description": "Consulting Services - January", "quantity": 10, "unit_price": 1500.00, "amount": 15000.00},
                {"description": "Software License", "quantity": 1, "unit_price": 5000.00, "amount": 5000.00},
            ],
            "subtotal": 20000.00,
            "tax_amount": 3000.00,
            "tax_rate": 15,
            "total_amount": 23000.00,
            "currency": "ZAR",
            "payment_terms": "Net 30",
            "bank_details": "FNB Account: 62****4321, Branch: 250655",
        },
        "validation": {"is_expired": None, "expiry_date": None, "issues": []},
    },
    "bill": {
        "document_type": "bill",
        "document_subtype": "phone_bill",
        "title": "[MOCK] Vodacom Monthly Bill - January 2024",
        "confidence": 0.96,
        "quality": {"readable": True, "issues": []},
        "content": {
            "account_holder": "Jane Mock Smith",
            "provider_name": "Vodacom",
            "account_number": "VOD-123456789",
            "billing_period": {"from": "2024-01-01", "to": "2024-01-31"},
            "bill_date": "2024-01-31",
            "due_date": "2024-02-20",
            "previous_balance": 0.00,
            "payments_received": 0.00,
            "current_charges": 899.00,
            "total_due": 899.00,
            "currency": "ZAR",
            "line_items": [
                {"description": "Monthly Contract - Unlimited Talk & Text", "amount": 599.00},
                {"description": "Data Add-on 20GB", "amount": 200.00},
                {"description": "VAT (15%)", "amount": 100.00},
            ],
        },
        "validation": {"is_expired": None, "expiry_date": None, "issues": []},
    },
    "address": {
        "document_type": "proof_of_address",
        "document_subtype": "utility_bill",
        "title": "[MOCK] Electricity Bill - January 2024",
        "confidence": 0.96,
        "quality": {"readable": True, "issues": []},
        "content": {
            "full_name": "Jane Mock Smith",
            "address": {
                "line1": "123 Mock Street",
                "line2": None,
                "city": "Cape Town",
                "state_province": "Western Cape",
                "postal_code": "8001",
                "country": "South Africa",
            },
            "document_date": "2024-01-15",
            "issuer": "City of Cape Town",
            "account_number": "UTIL-9876543",
            "is_within_3_months": True,
        },
        "validation": {"is_expired": None, "expiry_date": None, "issues": []},
    },
}


class MockProvider(LLMProvider):
    def __init__(self, **kwargs):
        self._filename_hint: str = kwargs.get("filename_hint", "")

    async def analyze_document(
        self,
        text: str | None = None,
        images: list[bytes] | None = None,
        extraction_hint: str | None = None,
    ) -> dict[str, Any]:
        hint_text = f"{self._filename_hint} {extraction_hint or ''}".lower()

        if any(k in hint_text for k in ("identity", "passport", "national_id", "license", "licence", "permit")):
            doc_type = "identity"
        elif any(k in hint_text for k in ("bank", "statement", "account")):
            doc_type = "bank"
        elif any(k in hint_text for k in ("payslip", "salary", "pay", "wage")):
            doc_type = "payslip"
        elif any(k in hint_text for k in ("invoice", "proforma", "tax_invoice", "commercial_invoice")):
            doc_type = "invoice"
        elif any(k in hint_text for k in ("phone_bill", "medical_bill", "subscription", "recurring")):
            doc_type = "bill"
        elif any(k in hint_text for k in ("proof_of_address", "proof of address", "utility", "municipal", "lease")):
            doc_type = "address"
        else:
            doc_type = _detect_type(self._filename_hint)

        logger.info("mock_provider_returning", extra={"doc_type": doc_type})
        return _SAMPLES[doc_type].copy()
