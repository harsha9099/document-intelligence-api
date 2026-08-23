import uuid
from typing import Any

from app.models.schemas import StoredDocument


class InMemoryDocumentRepository:
    def __init__(self) -> None:
        self._store: dict[str, StoredDocument] = {}

    def save(self, response: Any, filename: str) -> StoredDocument:
        doc_id = str(uuid.uuid4())
        stored = StoredDocument(id=doc_id, filename=filename, **response.model_dump())
        self._store[doc_id] = stored
        return stored

    def get(self, doc_id: str) -> StoredDocument | None:
        return self._store.get(doc_id)

    def list_all(self) -> list[StoredDocument]:
        return list(self._store.values())

    def clear(self) -> None:
        self._store.clear()
