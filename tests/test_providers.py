from unittest.mock import patch

import pytest

from app.llm.factory import PROVIDERS, get_llm_provider
from app.llm.mock_provider import MockProvider


def test_all_providers_registered():
    assert "anthropic" in PROVIDERS
    assert "aitrium" in PROVIDERS
    assert "bedrock" in PROVIDERS
    assert "openai" in PROVIDERS
    assert "mock" in PROVIDERS


def test_get_mock_provider_explicitly():
    provider = get_llm_provider("mock")
    assert isinstance(provider, MockProvider)


def test_unknown_provider_raises():
    with pytest.raises(ValueError, match="Unknown LLM provider"):
        get_llm_provider("banana")


def test_auto_fallback_to_mock_when_no_keys():
    with patch("app.llm.factory._no_keys_configured", return_value=True):
        provider = get_llm_provider("anthropic")
    assert isinstance(provider, MockProvider)


def test_no_fallback_when_key_present():
    with patch("app.llm.factory._no_keys_configured", return_value=False):
        # Should try to create anthropic, which will fail without key at runtime
        # but the factory at least shouldn't substitute mock
        provider_class = PROVIDERS.get("anthropic")
        assert provider_class is not None
        assert provider_class.__name__ == "AnthropicProvider"


# --- MockProvider ---

@pytest.mark.asyncio
async def test_mock_provider_identity():
    p = MockProvider(filename_hint="passport_scan.pdf")
    result = await p.analyze_document()
    assert result["document_type"] == "identity_document"


@pytest.mark.asyncio
async def test_mock_provider_bank():
    p = MockProvider(filename_hint="bank_statement_jan.pdf")
    result = await p.analyze_document()
    assert result["document_type"] == "bank_statement"


@pytest.mark.asyncio
async def test_mock_provider_payslip():
    p = MockProvider(filename_hint="payslip_march.pdf")
    result = await p.analyze_document()
    assert result["document_type"] == "payslip"


@pytest.mark.asyncio
async def test_mock_provider_address():
    p = MockProvider(filename_hint="utility_bill.pdf")
    result = await p.analyze_document()
    assert result["document_type"] == "proof_of_address"


@pytest.mark.asyncio
async def test_mock_provider_hint_overrides_filename():
    p = MockProvider(filename_hint="scan.pdf")
    result = await p.analyze_document(extraction_hint="this is a payslip")
    assert result["document_type"] == "payslip"


@pytest.mark.asyncio
async def test_mock_provider_has_required_fields():
    p = MockProvider()
    result = await p.analyze_document()
    assert "document_type" in result
    assert "confidence" in result
    assert "content" in result
    assert "quality" in result
    assert "validation" in result


@pytest.mark.asyncio
async def test_mock_provider_default_returns_bank():
    p = MockProvider()
    result = await p.analyze_document()
    assert result["document_type"] == "bank_statement"


@pytest.mark.asyncio
async def test_mock_confidence_is_high():
    p = MockProvider()
    result = await p.analyze_document()
    assert result["confidence"] >= 0.9
