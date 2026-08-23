import json
import uuid
from typing import Any

import aiosqlite

from app.models.schemas import DocumentQuality, DocumentValidation, StoredDocument
from app.repositories.base import DocumentRepository


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
                created_at TEXT DEFAULT (datetime('now'))
            )
        """)
        await db.commit()

    async def save(self, response: Any, filename: str) -> StoredDocument:
        doc_id = str(uuid.uuid4())
        stored = StoredDocument(id=doc_id, filename=filename, **response.model_dump())
        async with aiosqlite.connect(self._db_path) as db:
            await self._init(db)
            await db.execute(
                """INSERT INTO documents
                   (id, filename, document_type, document_subtype, title, confidence,
                    content, quality, validation, raw_text)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (
                    stored.id,
                    stored.filename,
                    stored.document_type,
                    stored.document_subtype,
                    stored.title,
                    stored.confidence,
                    json.dumps(stored.content),
                    json.dumps(stored.quality.model_dump() if stored.quality else None),
                    json.dumps(stored.validation.model_dump() if stored.validation else None),
                    stored.raw_text,
                ),
            )
            await db.commit()
        return stored

    async def get(self, doc_id: str) -> StoredDocument | None:
        async with aiosqlite.connect(self._db_path) as db:
            await self._init(db)
            db.row_factory = aiosqlite.Row
            async with db.execute(
                "SELECT * FROM documents WHERE id = ?", (doc_id,)
            ) as cursor:
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
    return StoredDocument(
        id=row["id"],
        filename=row["filename"],
        document_type=row["document_type"],
        document_subtype=row["document_subtype"],
        title=row["title"],
        confidence=row["confidence"],
        content=json.loads(row["content"]),
        quality=DocumentQuality(**quality_raw) if quality_raw else None,
        validation=DocumentValidation(**validation_raw) if validation_raw else None,
        raw_text=row["raw_text"],
    )
