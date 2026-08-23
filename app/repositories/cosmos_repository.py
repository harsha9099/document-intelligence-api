from typing import Any

from app.repositories.base import DocumentRepository


class CosmosDocumentRepository(DocumentRepository):
    """Azure Cosmos DB backend — not yet implemented."""

    async def save(self, response: Any, filename: str):
        raise NotImplementedError("Cosmos DB repository not yet implemented")

    async def get(self, doc_id: str):
        raise NotImplementedError("Cosmos DB repository not yet implemented")

    async def list_all(self, limit: int = 100, offset: int = 0, document_type: str | None = None):
        raise NotImplementedError("Cosmos DB repository not yet implemented")

    async def delete(self, doc_id: str) -> bool:
        raise NotImplementedError("Cosmos DB repository not yet implemented")
