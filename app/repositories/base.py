from abc import ABC, abstractmethod

from app.models.schemas import StoredDocument


class DocumentRepository(ABC):
    @abstractmethod
    async def save(self, document: StoredDocument) -> StoredDocument:
        ...

    @abstractmethod
    async def get(self, doc_id: str) -> StoredDocument | None:
        ...

    @abstractmethod
    async def list_all(
        self, limit: int = 100, offset: int = 0, document_type: str | None = None
    ) -> list[StoredDocument]:
        ...

    @abstractmethod
    async def delete(self, doc_id: str) -> bool:
        ...
