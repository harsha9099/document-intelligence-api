import logging
from typing import Any

logger = logging.getLogger(__name__)

_MOCK_IDENTITY = {
    "document_type": "identity_document",
    "document_subtype": "national_id",
    "title": "National ID (Azure DI)",
    "confidence": 0.91,
    "quality": {"readable": True, "issues": []},
    "content": {
        "full_name": "Jane Doe",
        "first_name": "Jane",
        "last_name": "Doe",
        "id_number": "8901015009087",
        "date_of_birth": "1989-01-01",
        "gender": "F",
        "nationality": "South African",
        "country_of_issue": "ZA",
        "issue_date": None,
        "expiry_date": None,
        "address": None,
        "photo_present": True,
        "signature_present": True,
        "document_number": None,
        "machine_readable_zone": None,
    },
    "validation": {"is_expired": False, "expiry_date": None, "issues": []},
}

_MOCK_INVOICE = {
    "document_type": "invoice",
    "document_subtype": "tax_invoice",
    "title": "Tax Invoice (Azure DI)",
    "confidence": 0.93,
    "quality": {"readable": True, "issues": []},
    "content": {
        "vendor_name": "Acme Corp",
        "vendor_address": "1 Business Park, Johannesburg",
        "customer_name": "Client Ltd",
        "customer_address": None,
        "invoice_number": "INV-2024-001",
        "invoice_date": "2024-01-15",
        "due_date": "2024-02-15",
        "purchase_order_number": None,
        "line_items": [{"description": "Professional Services", "quantity": 1, "unit_price": 5000.0, "amount": 5000.0}],
        "subtotal": 5000.0,
        "tax_amount": 750.0,
        "tax_rate": 15.0,
        "total_amount": 5750.0,
        "currency": "ZAR",
        "payment_terms": "30 days",
        "bank_details": None,
    },
    "validation": {"is_expired": None, "expiry_date": None, "issues": []},
}


def _is_configured() -> bool:
    from app.config import settings
    return bool(settings.azure_di_endpoint and settings.azure_di_key)


def _make_client():
    from app.config import settings
    from azure.ai.formrecognizer import DocumentAnalysisClient
    from azure.core.credentials import AzureKeyCredential
    return DocumentAnalysisClient(settings.azure_di_endpoint, AzureKeyCredential(settings.azure_di_key))


def _map_confidence(fields: dict) -> float:
    confidences = [f.confidence for f in fields.values() if hasattr(f, "confidence") and f.confidence is not None]
    return round(sum(confidences) / len(confidences), 3) if confidences else 0.75


def _field_value(field) -> Any:
    if field is None:
        return None
    if hasattr(field, "value"):
        v = field.value
        if hasattr(v, "isoformat"):
            return v.isoformat()
        if hasattr(v, "amount"):
            return v.amount
        return v
    return None


async def analyze_identity_document(file_bytes: bytes) -> dict | None:
    if not _is_configured():
        logger.debug("Azure DI not configured, using mock identity response")
        return _MOCK_IDENTITY

    try:
        client = _make_client()
        poller = client.begin_analyze_document("prebuilt-idDocument", file_bytes)
        result = poller.result()
        if not result.documents:
            return None
        doc = result.documents[0]
        f = doc.fields
        confidence = _map_confidence(f)
        dob = _field_value(f.get("DateOfBirth"))
        expiry = _field_value(f.get("DateOfExpiration"))
        return {
            "document_type": "identity_document",
            "document_subtype": doc.doc_type.replace("idDocument.", "") if doc.doc_type else "national_id",
            "title": f"Identity Document (Azure DI)",
            "confidence": confidence,
            "quality": {"readable": True, "issues": []},
            "content": {
                "full_name": _field_value(f.get("FirstName")) and f"{_field_value(f.get('FirstName'))} {_field_value(f.get('LastName'))}".strip(),
                "first_name": _field_value(f.get("FirstName")),
                "last_name": _field_value(f.get("LastName")),
                "id_number": _field_value(f.get("DocumentNumber")),
                "date_of_birth": dob[:10] if dob else None,
                "gender": _field_value(f.get("Sex")),
                "nationality": _field_value(f.get("Nationality")),
                "country_of_issue": _field_value(f.get("CountryRegion")),
                "issue_date": None,
                "expiry_date": expiry[:10] if expiry else None,
                "address": _field_value(f.get("Address")),
                "photo_present": True,
                "signature_present": False,
                "document_number": _field_value(f.get("DocumentNumber")),
                "machine_readable_zone": _field_value(f.get("MachineReadableZone")),
            },
            "validation": {
                "is_expired": False,
                "expiry_date": expiry[:10] if expiry else None,
                "issues": [],
            },
        }
    except Exception as e:
        logger.warning("Azure DI identity extraction failed: %s", e)
        return None


async def analyze_invoice(file_bytes: bytes) -> dict | None:
    if not _is_configured():
        logger.debug("Azure DI not configured, using mock invoice response")
        return _MOCK_INVOICE

    try:
        client = _make_client()
        poller = client.begin_analyze_document("prebuilt-invoice", file_bytes)
        result = poller.result()
        if not result.documents:
            return None
        doc = result.documents[0]
        f = doc.fields
        confidence = _map_confidence(f)

        line_items = []
        for item in (_field_value(f.get("Items")) or []):
            item_fields = item.value if hasattr(item, "value") else {}
            line_items.append({
                "description": _field_value(item_fields.get("Description")),
                "quantity": _field_value(item_fields.get("Quantity")),
                "unit_price": _field_value(item_fields.get("UnitPrice")),
                "amount": _field_value(item_fields.get("Amount")),
            })

        invoice_date = _field_value(f.get("InvoiceDate"))
        due_date = _field_value(f.get("DueDate"))
        return {
            "document_type": "invoice",
            "document_subtype": "tax_invoice",
            "title": f"Invoice {_field_value(f.get('InvoiceId')) or ''} (Azure DI)".strip(),
            "confidence": confidence,
            "quality": {"readable": True, "issues": []},
            "content": {
                "vendor_name": _field_value(f.get("VendorName")),
                "vendor_address": _field_value(f.get("VendorAddress")),
                "customer_name": _field_value(f.get("CustomerName")),
                "customer_address": _field_value(f.get("CustomerAddress")),
                "invoice_number": _field_value(f.get("InvoiceId")),
                "invoice_date": invoice_date[:10] if invoice_date else None,
                "due_date": due_date[:10] if due_date else None,
                "purchase_order_number": _field_value(f.get("PurchaseOrder")),
                "line_items": line_items,
                "subtotal": _field_value(f.get("SubTotal")),
                "tax_amount": _field_value(f.get("TotalTax")),
                "tax_rate": None,
                "total_amount": _field_value(f.get("InvoiceTotal")),
                "currency": None,
                "payment_terms": None,
                "bank_details": None,
            },
            "validation": {"is_expired": None, "expiry_date": None, "issues": []},
        }
    except Exception as e:
        logger.warning("Azure DI invoice extraction failed: %s", e)
        return None


async def analyze_receipt(file_bytes: bytes) -> dict | None:
    if not _is_configured():
        return None

    try:
        client = _make_client()
        poller = client.begin_analyze_document("prebuilt-receipt", file_bytes)
        result = poller.result()
        if not result.documents:
            return None
        doc = result.documents[0]
        f = doc.fields
        confidence = _map_confidence(f)
        tx_date = _field_value(f.get("TransactionDate"))
        return {
            "document_type": "bill",
            "document_subtype": "other_bill",
            "title": f"Receipt from {_field_value(f.get('MerchantName')) or 'unknown'} (Azure DI)",
            "confidence": confidence,
            "quality": {"readable": True, "issues": []},
            "content": {
                "account_holder": None,
                "provider_name": _field_value(f.get("MerchantName")),
                "account_number": None,
                "billing_period": None,
                "bill_date": tx_date[:10] if tx_date else None,
                "due_date": None,
                "previous_balance": None,
                "payments_received": None,
                "current_charges": _field_value(f.get("Subtotal")),
                "total_due": _field_value(f.get("Total")),
                "currency": None,
                "line_items": [],
            },
            "validation": {"is_expired": None, "expiry_date": None, "issues": []},
        }
    except Exception as e:
        logger.warning("Azure DI receipt extraction failed: %s", e)
        return None


async def analyze_general(file_bytes: bytes) -> dict | None:
    if not _is_configured():
        return None

    try:
        client = _make_client()
        poller = client.begin_analyze_document("prebuilt-layout", file_bytes)
        result = poller.result()
        text = "\n".join(p.content for p in result.paragraphs) if result.paragraphs else ""
        if not text.strip():
            return None
        return {
            "document_type": "unknown",
            "document_subtype": None,
            "title": "Document (Azure DI Layout)",
            "confidence": 0.5,
            "quality": {"readable": True, "issues": []},
            "content": {"raw_layout_text": text[:2000]},
            "validation": {"is_expired": None, "expiry_date": None, "issues": []},
        }
    except Exception as e:
        logger.warning("Azure DI general extraction failed: %s", e)
        return None
