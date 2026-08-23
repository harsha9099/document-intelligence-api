import uuid
from typing import Any

from app.models.schemas import StoredDocument
from app.repositories.base import DocumentRepository


class InMemoryDocumentRepository(DocumentRepository):
    def __init__(self) -> None:
        self._store: dict[str, StoredDocument] = {}

    async def save(self, response: Any, filename: str) -> StoredDocument:
        doc_id = str(uuid.uuid4())
        stored = StoredDocument(id=doc_id, filename=filename, **response.model_dump())
        self._store[doc_id] = stored
        return stored

    async def get(self, doc_id: str) -> StoredDocument | None:
        return self._store.get(doc_id)

    async def list_all(
        self, limit: int = 100, offset: int = 0, document_type: str | None = None
    ) -> list[StoredDocument]:
        docs = list(self._store.values())
        if document_type:
            docs = [d for d in docs if d.document_type == document_type]
        return docs[offset : offset + limit]

    async def delete(self, doc_id: str) -> bool:
        return self._store.pop(doc_id, None) is not None

    def clear(self) -> None:
        self._store.clear()
