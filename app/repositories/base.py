from abc import ABC, abstractmethod
from typing import Any


class DocumentRepository(ABC):
    @abstractmethod
    async def save(self, response: Any, filename: str):
        ...

    @abstractmethod
    async def get(self, doc_id: str):
        ...

    @abstractmethod
    async def list_all(self, limit: int = 100, offset: int = 0, document_type: str | None = None):
        ...

    @abstractmethod
    async def delete(self, doc_id: str) -> bool:
        ...
