from typing import Any

from app.repositories.base import DocumentRepository


class TableStorageDocumentRepository(DocumentRepository):
    """Azure Table Storage backend — not yet implemented."""

    async def save(self, response: Any, filename: str):
        raise NotImplementedError("Azure Table Storage repository not yet implemented")

    async def get(self, doc_id: str):
        raise NotImplementedError("Azure Table Storage repository not yet implemented")

    async def list_all(self, limit: int = 100, offset: int = 0, document_type: str | None = None):
        raise NotImplementedError("Azure Table Storage repository not yet implemented")

    async def delete(self, doc_id: str) -> bool:
        raise NotImplementedError("Azure Table Storage repository not yet implemented")
