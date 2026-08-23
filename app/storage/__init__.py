from app.storage.base import FileStorage


def create_storage() -> FileStorage:
    from app.config import settings

    backend = settings.storage_backend.lower()

    if backend == "azure_blob":
        from app.storage.azure_blob_storage import AzureBlobStorage
        return AzureBlobStorage()

    from app.storage.local_storage import LocalFileStorage
    return LocalFileStorage(settings.storage_path)
