import json

import aiosqlite

from app.models.schemas import DocumentQuality, DocumentValidation, StoredDocument
from app.repositories.base import DocumentRepository

_NEW_COLUMNS = [
    ("file_size_bytes", "INTEGER DEFAULT 0"),
    ("file_content_type", "TEXT DEFAULT ''"),
    ("storage_path", "TEXT"),
    ("uploaded_at", "TEXT DEFAULT ''"),
    ("processed_at", "TEXT DEFAULT ''"),
    ("processing_duration_ms", "INTEGER DEFAULT 0"),
    ("provider_used", "TEXT DEFAULT ''"),
    ("model_used", "TEXT"),
    ("page_count", "INTEGER"),
    ("extraction_metadata", "TEXT DEFAULT '{}'"),
]


class SqliteDocumentRepository(DocumentRepository):
    def __init__(self, db_path: str = "documents.db") -> None:
        self._db_path = db_path

    async def _init(self, db: aiosqlite.Connection) -> None:
        await db.execute("""
            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                filename TEXT,
                document_type TEXT,
                document_subtype TEXT,
                title TEXT,
                confidence REAL,
                content TEXT,
                quality TEXT,
                validation TEXT,
                raw_text TEXT,
                created_at TEXT DEFAULT (datetime('now')),
                file_size_bytes INTEGER DEFAULT 0,
                file_content_type TEXT DEFAULT '',
                storage_path TEXT,
                uploaded_at TEXT DEFAULT '',
                processed_at TEXT DEFAULT '',
                processing_duration_ms INTEGER DEFAULT 0,
                provider_used TEXT DEFAULT '',
                model_used TEXT,
                page_count INTEGER,
                extraction_metadata TEXT DEFAULT '{}'
            )
        """)
        # Migrate existing DBs: add any missing columns
        for col_name, col_def in _NEW_COLUMNS:
            try:
                await db.execute(f"ALTER TABLE documents ADD COLUMN {col_name} {col_def}")
            except Exception:
                pass  # Column already exists
        await db.commit()

    async def save(self, document: StoredDocument) -> StoredDocument:
        async with aiosqlite.connect(self._db_path) as db:
            await self._init(db)
            await db.execute(
                """INSERT OR REPLACE INTO documents
                   (id, filename, document_type, document_subtype, title, confidence,
                    content, quality, validation, raw_text,
                    file_size_bytes, file_content_type, storage_path,
                    uploaded_at, processed_at, processing_duration_ms,
                    provider_used, model_used, page_count, extraction_metadata)
                   VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (
                    document.id,
                    document.filename,
                    document.document_type,
                    document.document_subtype,
                    document.title,
                    document.confidence,
                    json.dumps(document.content),
                    json.dumps(document.quality.model_dump() if document.quality else None),
                    json.dumps(document.validation.model_dump() if document.validation else None),
                    document.raw_text,
                    document.file_size_bytes,
                    document.file_content_type,
                    document.storage_path,
                    document.uploaded_at,
                    document.processed_at,
                    document.processing_duration_ms,
                    document.provider_used,
                    document.model_used,
                    document.page_count,
                    json.dumps(document.extraction_metadata),
                ),
            )
            await db.commit()
        return document

    async def get(self, doc_id: str) -> StoredDocument | None:
        async with aiosqlite.connect(self._db_path) as db:
            await self._init(db)
            db.row_factory = aiosqlite.Row
            async with db.execute("SELECT * FROM documents WHERE id = ?", (doc_id,)) as cursor:
                row = await cursor.fetchone()
                return _row_to_doc(row) if row else None

    async def list_all(
        self, limit: int = 100, offset: int = 0, document_type: str | None = None
    ) -> list[StoredDocument]:
        async with aiosqlite.connect(self._db_path) as db:
            await self._init(db)
            db.row_factory = aiosqlite.Row
            if document_type:
                async with db.execute(
                    "SELECT * FROM documents WHERE document_type = ? ORDER BY created_at DESC LIMIT ? OFFSET ?",
                    (document_type, limit, offset),
                ) as cursor:
                    rows = await cursor.fetchall()
            else:
                async with db.execute(
                    "SELECT * FROM documents ORDER BY created_at DESC LIMIT ? OFFSET ?",
                    (limit, offset),
                ) as cursor:
                    rows = await cursor.fetchall()
            return [_row_to_doc(r) for r in rows]

    async def delete(self, doc_id: str) -> bool:
        async with aiosqlite.connect(self._db_path) as db:
            await self._init(db)
            cursor = await db.execute("DELETE FROM documents WHERE id = ?", (doc_id,))
            await db.commit()
            return cursor.rowcount > 0

    def clear(self) -> None:
        pass


def _row_to_doc(row: aiosqlite.Row) -> StoredDocument:
    quality_raw = json.loads(row["quality"]) if row["quality"] else None
    validation_raw = json.loads(row["validation"]) if row["validation"] else None
    metadata_raw = row["extraction_metadata"] if "extraction_metadata" in row.keys() else "{}"
    return StoredDocument(
        id=row["id"],
        filename=row["filename"] or "",
        document_type=row["document_type"],
        document_subtype=row["document_subtype"],
        title=row["title"],
        confidence=row["confidence"],
        content=json.loads(row["content"]),
        quality=DocumentQuality(**quality_raw) if quality_raw else None,
        validation=DocumentValidation(**validation_raw) if validation_raw else None,
        raw_text=row["raw_text"],
        file_size_bytes=row["file_size_bytes"] if "file_size_bytes" in row.keys() else 0,
        file_content_type=row["file_content_type"] if "file_content_type" in row.keys() else "",
        storage_path=row["storage_path"] if "storage_path" in row.keys() else None,
        uploaded_at=row["uploaded_at"] if "uploaded_at" in row.keys() else "",
        processed_at=row["processed_at"] if "processed_at" in row.keys() else "",
        processing_duration_ms=row["processing_duration_ms"] if "processing_duration_ms" in row.keys() else 0,
        provider_used=row["provider_used"] if "provider_used" in row.keys() else "",
        model_used=row["model_used"] if "model_used" in row.keys() else None,
        page_count=row["page_count"] if "page_count" in row.keys() else None,
        extraction_metadata=json.loads(metadata_raw) if metadata_raw else {},
    )
