from app.storage.base import FileStorage


class AzureBlobStorage(FileStorage):
    """Azure Blob Storage implementation — stub, not yet implemented."""

    async def save(self, file_id: str, filename: str, file_bytes: bytes) -> str:
        raise NotImplementedError("AzureBlobStorage not implemented")

    async def get(self, file_id: str) -> bytes | None:
        raise NotImplementedError("AzureBlobStorage not implemented")

    async def delete(self, file_id: str) -> bool:
        raise NotImplementedError("AzureBlobStorage not implemented")

    def get_filename(self, file_id: str) -> str | None:
        raise NotImplementedError("AzureBlobStorage not implemented")
