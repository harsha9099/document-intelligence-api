from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from httpx import ASGITransport, AsyncClient

from app.main import app, _repository
from app.models.schemas import DocumentQuality, DocumentResponse, DocumentValidation, StoredDocument


@pytest.fixture(autouse=True)
def clear_repository():
    _repository.clear()
    yield
    _repository.clear()


@pytest.fixture
def client():
    return AsyncClient(transport=ASGITransport(app=app), base_url="http://test")


def _mock_stored(doc_type: str = "bank_statement", filename: str = "doc.pdf") -> StoredDocument:
    return StoredDocument(
        id="test-id-123",
        filename=filename,
        document_type=doc_type,
        document_subtype="current_account",
        title="Test Doc",
        confidence=0.95,
        quality=DocumentQuality(readable=True, issues=[]),
        content={"bank_name": "Test Bank"},
        validation=DocumentValidation(is_expired=False, expiry_date=None, issues=[]),
        raw_text=None,
    )


async def test_health(client):
    response = await client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "healthy"}


# --- /extract validation ---

async def test_extract_rejects_unsupported_extension(client):
    response = await client.post(
        "/extract",
        files={"file": ("test.exe", b"fake", "application/octet-stream")},
    )
    assert response.status_code == 400
    assert "not supported" in response.json()["detail"]


async def test_extract_rejects_missing_filename(client):
    response = await client.post(
        "/extract",
        files={"file": ("", b"fake", "application/octet-stream")},
    )
    assert response.status_code == 400


async def test_extract_rejects_invalid_provider(client):
    response = await client.post(
        "/extract",
        files={"file": ("doc.pdf", b"%PDF-1.4", "application/pdf")},
        data={"provider": "does_not_exist"},
    )
    assert response.status_code == 400
    assert "Unknown LLM provider" in response.json()["detail"]


async def test_extract_rejects_oversized_file(client):
    with patch("app.main.settings") as mock_settings:
        mock_settings.allowed_extensions_list = ["pdf"]
        mock_settings.max_file_size_bytes = 10
        mock_settings.max_file_size_mb = 0
        response = await client.post(
            "/extract",
            files={"file": ("doc.pdf", b"x" * 100, "application/pdf")},
        )
    assert response.status_code == 400
    assert "exceeds" in response.json()["detail"]


# --- /extract success ---

@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_extract_success_returns_stored_document(mock_provider, mock_process, client):
    mock_llm = MagicMock()
    mock_provider.return_value = mock_llm
    mock_response = DocumentResponse(
        document_type="bank_statement",
        document_subtype="current_account",
        title="Chase Jan 2024",
        confidence=0.95,
        content={"bank_name": "Chase"},
    )
    mock_process.return_value = mock_response

    response = await client.post(
        "/extract",
        files={"file": ("statement.pdf", b"%PDF-1.4", "application/pdf")},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["document_type"] == "bank_statement"
    assert data["confidence"] == 0.95
    assert "id" in data
    assert data["filename"] == "statement.pdf"


@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_extract_with_type_hint(mock_provider, mock_process, client):
    mock_provider.return_value = MagicMock()
    mock_process.return_value = DocumentResponse(
        document_type="identity_document",
        title="ID",
        confidence=0.9,
        content={},
    )
    response = await client.post(
        "/extract",
        files={"file": ("id.jpg", b"fake", "image/jpeg")},
        data={"document_type": "identity_document"},
    )
    assert response.status_code == 200
    _, kwargs = mock_process.call_args
    assert "identity document" in (kwargs.get("extraction_hint") or "")


@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_extract_failure_returns_422(mock_provider, mock_process, client):
    mock_provider.return_value = MagicMock()
    mock_process.side_effect = RuntimeError("LLM timeout")

    response = await client.post(
        "/extract",
        files={"file": ("doc.pdf", b"%PDF-1.4", "application/pdf")},
    )
    assert response.status_code == 422
    assert "processing failed" in response.json()["detail"]


# --- /extract/batch ---

@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_batch_extract_success(mock_provider, mock_process, client):
    mock_provider.return_value = MagicMock()
    mock_process.return_value = DocumentResponse(
        document_type="payslip",
        title="Payslip",
        confidence=0.9,
        content={},
    )
    response = await client.post(
        "/extract/batch",
        files=[
            ("files", ("p1.pdf", b"%PDF-1.4", "application/pdf")),
            ("files", ("p2.pdf", b"%PDF-1.4", "application/pdf")),
        ],
    )
    assert response.status_code == 200
    assert len(response.json()) == 2


async def test_batch_rejects_more_than_10(client):
    files = [("files", (f"f{i}.pdf", b"%PDF", "application/pdf")) for i in range(11)]
    response = await client.post("/extract/batch", files=files)
    assert response.status_code == 400
    assert "10" in response.json()["detail"]


@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_batch_skips_failed_files(mock_provider, mock_process, client):
    mock_provider.return_value = MagicMock()
    mock_process.side_effect = [
        DocumentResponse(document_type="payslip", title="OK", confidence=0.9, content={}),
        RuntimeError("failed"),
    ]
    response = await client.post(
        "/extract/batch",
        files=[
            ("files", ("good.pdf", b"%PDF-1.4", "application/pdf")),
            ("files", ("bad.pdf", b"%PDF-1.4", "application/pdf")),
        ],
    )
    assert response.status_code == 200
    assert len(response.json()) == 1


# --- /documents ---

@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_list_documents_empty(mock_provider, mock_process, client):
    response = await client.get("/documents")
    assert response.status_code == 200
    assert response.json() == []


@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_list_documents_after_extract(mock_provider, mock_process, client):
    mock_provider.return_value = MagicMock()
    mock_process.return_value = DocumentResponse(
        document_type="bank_statement", title="Statement", confidence=0.9, content={}
    )
    await client.post(
        "/extract",
        files={"file": ("s.pdf", b"%PDF-1.4", "application/pdf")},
    )
    response = await client.get("/documents")
    assert response.status_code == 200
    assert len(response.json()) == 1


@patch("app.main.process_document")
@patch("app.main.get_llm_provider")
async def test_get_document_by_id(mock_provider, mock_process, client):
    mock_provider.return_value = MagicMock()
    mock_process.return_value = DocumentResponse(
        document_type="payslip", title="P", confidence=0.8, content={}
    )
    extract_resp = await client.post(
        "/extract",
        files={"file": ("p.pdf", b"%PDF-1.4", "application/pdf")},
    )
    doc_id = extract_resp.json()["id"]

    get_resp = await client.get(f"/documents/{doc_id}")
    assert get_resp.status_code == 200
    assert get_resp.json()["id"] == doc_id


async def test_get_document_not_found(client):
    response = await client.get("/documents/nonexistent-id")
    assert response.status_code == 404
