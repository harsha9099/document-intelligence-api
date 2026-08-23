from unittest.mock import AsyncMock, patch

import pytest
from httpx import ASGITransport, AsyncClient

from app.main import app


@pytest.fixture
def client():
    return AsyncClient(transport=ASGITransport(app=app), base_url="http://test")


async def test_health(client):
    response = await client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "healthy"}


async def test_extract_rejects_unsupported_extension(client):
    response = await client.post(
        "/extract",
        files={"file": ("test.exe", b"fake content", "application/octet-stream")},
    )
    assert response.status_code == 400
    assert "not supported" in response.json()["detail"]


async def test_extract_rejects_missing_filename(client):
    response = await client.post(
        "/extract",
        files={"file": ("", b"fake content", "application/octet-stream")},
    )
    assert response.status_code == 400


@patch("app.services.document_service.process_document")
async def test_extract_success(mock_process, client):
    mock_process.return_value = AsyncMock(
        document_type="bank_statement",
        title="Chase Statement Jan 2024",
        confidence=0.95,
        content={"bank_name": "Chase", "transactions": []},
        raw_text="some text",
    )

    response = await client.post(
        "/extract",
        files={"file": ("statement.pdf", b"%PDF-1.4 fake", "application/pdf")},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["document_type"] == "bank_statement"
    assert data["confidence"] == 0.95


async def test_extract_invalid_provider(client):
    response = await client.post(
        "/extract",
        files={"file": ("doc.pdf", b"%PDF-1.4 fake", "application/pdf")},
        data={"provider": "nonexistent"},
    )
    assert response.status_code == 400
    assert "Unknown LLM provider" in response.json()["detail"]
