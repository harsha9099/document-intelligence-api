from typing import Any

from app.repositories.base import DocumentRepository


class SqlDocumentRepository(DocumentRepository):
    """SQL Server / PostgreSQL backend — not yet implemented."""

    async def save(self, response: Any, filename: str):
        raise NotImplementedError("SQL repository not yet implemented")

    async def get(self, doc_id: str):
        raise NotImplementedError("SQL repository not yet implemented")

    async def list_all(self, limit: int = 100, offset: int = 0, document_type: str | None = None):
        raise NotImplementedError("SQL repository not yet implemented")

    async def delete(self, doc_id: str) -> bool:
        raise NotImplementedError("SQL repository not yet implemented")
