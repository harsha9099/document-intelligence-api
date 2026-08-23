from abc import ABC, abstractmethod


class FileStorage(ABC):
    @abstractmethod
    async def save(self, file_id: str, filename: str, file_bytes: bytes) -> str:
        """Save file and return storage path/URI."""
        ...

    @abstractmethod
    async def get(self, file_id: str) -> bytes | None:
        ...

    @abstractmethod
    async def delete(self, file_id: str) -> bool:
        ...

    @abstractmethod
    def get_filename(self, file_id: str) -> str | None:
        ...
