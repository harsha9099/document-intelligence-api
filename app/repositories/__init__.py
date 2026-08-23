from app.repositories.base import DocumentRepository


def create_repository() -> DocumentRepository:
    from app.config import settings

    backend = settings.persistence_backend.lower()

    if backend == "memory":
        from app.repositories.memory_repository import InMemoryDocumentRepository
        return InMemoryDocumentRepository()
    elif backend == "sqlite":
        from app.repositories.sqlite_repository import SqliteDocumentRepository
        return SqliteDocumentRepository(settings.database_url)
    elif backend == "cosmos":
        from app.repositories.cosmos_repository import CosmosDocumentRepository
        return CosmosDocumentRepository()
    elif backend == "sql":
        from app.repositories.sql_repository import SqlDocumentRepository
        return SqlDocumentRepository()
    elif backend == "table_storage":
        from app.repositories.table_storage_repository import TableStorageDocumentRepository
        return TableStorageDocumentRepository()
    else:
        raise ValueError(f"Unknown persistence backend: {backend}. Available: memory, sqlite, cosmos, sql, table_storage")
