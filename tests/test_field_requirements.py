import pytest

from app.services.field_requirements import check_field_completeness


def test_identity_document_all_required_fields_present():
    complete, missing = check_field_completeness(
        "identity_document",
        {"id_number": "123", "full_name": "John", "date_of_birth": "1990-01-01"},
    )
    assert complete is True
    assert missing == []


def test_identity_document_missing_id_number():
    complete, missing = check_field_completeness(
        "identity_document",
        {"full_name": "John", "date_of_birth": "1990-01-01"},
    )
    assert complete is False
    assert missing == ["id_number"]


def test_invoice_all_required_fields_present():
    complete, missing = check_field_completeness(
        "invoice",
        {"invoice_number": "INV-1", "total_amount": 100, "vendor_name": "Acme"},
    )
    assert complete is True
    assert missing == []


def test_invoice_missing_total_amount_and_vendor_name():
    complete, missing = check_field_completeness(
        "invoice",
        {"invoice_number": "INV-1"},
    )
    assert complete is False
    assert sorted(missing) == sorted(["total_amount", "vendor_name"])


def test_unknown_document_type_returns_complete():
    complete, missing = check_field_completeness("unknown_type", {})
    assert complete is True
    assert missing == []


def test_bank_statement_all_required_fields_present():
    complete, missing = check_field_completeness(
        "bank_statement",
        {"account_number": "1234567890", "bank_name": "Test Bank", "transactions": []},
    )
    # transactions is a list — falsy when empty, so it counts as missing
    assert complete is False
    assert "transactions" in missing


def test_bank_statement_with_non_empty_transactions():
    complete, missing = check_field_completeness(
        "bank_statement",
        {"account_number": "1234567890", "bank_name": "Test Bank", "transactions": [{"amount": 50}]},
    )
    assert complete is True
    assert missing == []


def test_field_with_zero_value_is_not_treated_as_missing():
    # total_amount=0 should NOT count as missing (the function has a special case for 0)
    complete, missing = check_field_completeness(
        "invoice",
        {"invoice_number": "INV-0", "total_amount": 0, "vendor_name": "Acme"},
    )
    assert complete is True
    assert missing == []


def test_payslip_missing_all_required_fields():
    complete, missing = check_field_completeness("payslip", {})
    assert complete is False
    assert sorted(missing) == sorted(["employee_name", "gross_pay", "net_pay"])


def test_proof_of_address_all_required_fields_present():
    complete, missing = check_field_completeness(
        "proof_of_address",
        {"full_name": "Jane Doe", "address": "123 Main St"},
    )
    assert complete is True
    assert missing == []
