import pytest

from app.models.schemas import DocumentResponse, StoredDocument
from app.repositories.memory_repository import InMemoryDocumentRepository


def _sample_response(**kwargs) -> DocumentResponse:
    return DocumentResponse(
        document_type=kwargs.get("document_type", "bank_statement"),
        title=kwargs.get("title", "Test"),
        confidence=0.9,
        content={},
    )


def test_save_returns_stored_document():
    repo = InMemoryDocumentRepository()
    stored = repo.save(_sample_response(), "test.pdf")
    assert isinstance(stored, StoredDocument)
    assert stored.filename == "test.pdf"
    assert stored.id is not None


def test_save_assigns_unique_ids():
    repo = InMemoryDocumentRepository()
    a = repo.save(_sample_response(), "a.pdf")
    b = repo.save(_sample_response(), "b.pdf")
    assert a.id != b.id


def test_get_returns_saved_document():
    repo = InMemoryDocumentRepository()
    stored = repo.save(_sample_response(), "doc.pdf")
    retrieved = repo.get(stored.id)
    assert retrieved is not None
    assert retrieved.id == stored.id


def test_get_returns_none_for_missing_id():
    repo = InMemoryDocumentRepository()
    assert repo.get("nonexistent") is None


def test_list_all_empty():
    repo = InMemoryDocumentRepository()
    assert repo.list_all() == []


def test_list_all_returns_all_saved():
    repo = InMemoryDocumentRepository()
    repo.save(_sample_response(document_type="payslip"), "p.pdf")
    repo.save(_sample_response(document_type="bank_statement"), "b.pdf")
    results = repo.list_all()
    assert len(results) == 2
    types = {r.document_type for r in results}
    assert types == {"payslip", "bank_statement"}


def test_clear_removes_all_documents():
    repo = InMemoryDocumentRepository()
    repo.save(_sample_response(), "a.pdf")
    repo.save(_sample_response(), "b.pdf")
    repo.clear()
    assert repo.list_all() == []


def test_get_after_clear_returns_none():
    repo = InMemoryDocumentRepository()
    stored = repo.save(_sample_response(), "a.pdf")
    repo.clear()
    assert repo.get(stored.id) is None


def test_save_preserves_document_fields():
    repo = InMemoryDocumentRepository()
    resp = _sample_response(document_type="identity_document", title="My ID")
    stored = repo.save(resp, "id.pdf")
    assert stored.document_type == "identity_document"
    assert stored.title == "My ID"
    assert stored.confidence == 0.9
