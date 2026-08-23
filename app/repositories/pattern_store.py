import json
import logging
import uuid
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any

logger = logging.getLogger(__name__)


@dataclass
class StoredPattern:
    id: str
    document_type: str
    field_name: str
    pattern: str
    label: str | None = None
    confidence: float = 0.8
    hit_count: int = 0
    miss_count: int = 0
    last_hit_at: str | None = None
    created_at: str = ""
    updated_at: str = ""
    source: str = "manual"  # manual | learned | seeded
    active: bool = True
    metadata: dict[str, Any] = field(default_factory=dict)

    @property
    def success_rate(self) -> float:
        total = self.hit_count + self.miss_count
        return self.hit_count / total if total > 0 else 0.0

    @property
    def total_attempts(self) -> int:
        return self.hit_count + self.miss_count

    def to_dict(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "document_type": self.document_type,
            "field_name": self.field_name,
            "pattern": self.pattern,
            "label": self.label,
            "confidence": self.confidence,
            "hit_count": self.hit_count,
            "miss_count": self.miss_count,
            "last_hit_at": self.last_hit_at,
            "created_at": self.created_at,
            "updated_at": self.updated_at,
            "source": self.source,
            "active": self.active,
            "success_rate": round(self.success_rate, 3),
            "total_attempts": self.total_attempts,
            "metadata": self.metadata,
        }


@dataclass
class PatternAnalytics:
    total_patterns: int = 0
    active_patterns: int = 0
    total_hits: int = 0
    total_misses: int = 0
    avg_success_rate: float = 0.0
    top_patterns: list[dict] = field(default_factory=list)
    patterns_by_type: dict[str, int] = field(default_factory=dict)
    patterns_by_source: dict[str, int] = field(default_factory=dict)
    low_performing: list[dict] = field(default_factory=list)


class PatternRepository(ABC):
    @abstractmethod
    async def save(self, pattern: StoredPattern) -> StoredPattern:
        ...

    @abstractmethod
    async def get(self, pattern_id: str) -> StoredPattern | None:
        ...

    @abstractmethod
    async def get_by_type(self, document_type: str, active_only: bool = True) -> list[StoredPattern]:
        ...

    @abstractmethod
    async def get_all(self, active_only: bool = True) -> list[StoredPattern]:
        ...

    @abstractmethod
    async def update(self, pattern: StoredPattern) -> StoredPattern:
        ...

    @abstractmethod
    async def delete(self, pattern_id: str) -> bool:
        ...

    @abstractmethod
    async def record_hit(self, pattern_id: str) -> None:
        ...

    @abstractmethod
    async def record_miss(self, pattern_id: str) -> None:
        ...

    @abstractmethod
    async def get_analytics(self) -> PatternAnalytics:
        ...


class InMemoryPatternRepository(PatternRepository):
    def __init__(self):
        self._patterns: dict[str, StoredPattern] = {}

    async def save(self, pattern: StoredPattern) -> StoredPattern:
        now = datetime.now(timezone.utc).isoformat()
        if not pattern.id:
            pattern.id = str(uuid.uuid4())
        if not pattern.created_at:
            pattern.created_at = now
        pattern.updated_at = now
        self._patterns[pattern.id] = pattern
        return pattern

    async def get(self, pattern_id: str) -> StoredPattern | None:
        return self._patterns.get(pattern_id)

    async def get_by_type(self, document_type: str, active_only: bool = True) -> list[StoredPattern]:
        return [
            p for p in self._patterns.values()
            if p.document_type == document_type and (not active_only or p.active)
        ]

    async def get_all(self, active_only: bool = True) -> list[StoredPattern]:
        return [p for p in self._patterns.values() if not active_only or p.active]

    async def update(self, pattern: StoredPattern) -> StoredPattern:
        pattern.updated_at = datetime.now(timezone.utc).isoformat()
        self._patterns[pattern.id] = pattern
        return pattern

    async def delete(self, pattern_id: str) -> bool:
        return self._patterns.pop(pattern_id, None) is not None

    async def record_hit(self, pattern_id: str) -> None:
        if p := self._patterns.get(pattern_id):
            p.hit_count += 1
            p.last_hit_at = datetime.now(timezone.utc).isoformat()

    async def record_miss(self, pattern_id: str) -> None:
        if p := self._patterns.get(pattern_id):
            p.miss_count += 1

    async def get_analytics(self) -> PatternAnalytics:
        all_patterns = list(self._patterns.values())
        active = [p for p in all_patterns if p.active]
        rates = [p.success_rate for p in active if p.total_attempts > 0]

        by_type: dict[str, int] = {}
        by_source: dict[str, int] = {}
        for p in active:
            by_type[p.document_type] = by_type.get(p.document_type, 0) + 1
            by_source[p.source] = by_source.get(p.source, 0) + 1

        top = sorted(active, key=lambda p: p.hit_count, reverse=True)[:10]
        low = [p for p in active if p.total_attempts >= 10 and p.success_rate < 0.5]

        return PatternAnalytics(
            total_patterns=len(all_patterns),
            active_patterns=len(active),
            total_hits=sum(p.hit_count for p in all_patterns),
            total_misses=sum(p.miss_count for p in all_patterns),
            avg_success_rate=round(sum(rates) / len(rates), 3) if rates else 0.0,
            top_patterns=[p.to_dict() for p in top],
            patterns_by_type=by_type,
            patterns_by_source=by_source,
            low_performing=[p.to_dict() for p in low],
        )


class SqlitePatternRepository(PatternRepository):
    def __init__(self, db_path: str = "patterns.db"):
        self._db_path = db_path
        self._initialized = False

    async def _get_db(self):
        import aiosqlite
        db = await aiosqlite.connect(self._db_path)
        db.row_factory = aiosqlite.Row
        if not self._initialized:
            await db.execute("""
                CREATE TABLE IF NOT EXISTS patterns (
                    id TEXT PRIMARY KEY,
                    document_type TEXT NOT NULL,
                    field_name TEXT NOT NULL,
                    pattern TEXT NOT NULL,
                    label TEXT,
                    confidence REAL DEFAULT 0.8,
                    hit_count INTEGER DEFAULT 0,
                    miss_count INTEGER DEFAULT 0,
                    last_hit_at TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    source TEXT DEFAULT 'manual',
                    active INTEGER DEFAULT 1,
                    metadata TEXT DEFAULT '{}'
                )
            """)
            await db.execute(
                "CREATE INDEX IF NOT EXISTS idx_patterns_type ON patterns(document_type, active)"
            )
            await db.commit()
            self._initialized = True
        return db

    def _row_to_pattern(self, row) -> StoredPattern:
        return StoredPattern(
            id=row["id"],
            document_type=row["document_type"],
            field_name=row["field_name"],
            pattern=row["pattern"],
            label=row["label"],
            confidence=row["confidence"],
            hit_count=row["hit_count"],
            miss_count=row["miss_count"],
            last_hit_at=row["last_hit_at"],
            created_at=row["created_at"],
            updated_at=row["updated_at"],
            source=row["source"],
            active=bool(row["active"]),
            metadata=json.loads(row["metadata"]) if row["metadata"] else {},
        )

    async def save(self, pattern: StoredPattern) -> StoredPattern:
        now = datetime.now(timezone.utc).isoformat()
        if not pattern.id:
            pattern.id = str(uuid.uuid4())
        if not pattern.created_at:
            pattern.created_at = now
        pattern.updated_at = now

        db = await self._get_db()
        try:
            await db.execute(
                """INSERT INTO patterns (id, document_type, field_name, pattern, label,
                   confidence, hit_count, miss_count, last_hit_at, created_at, updated_at,
                   source, active, metadata) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (pattern.id, pattern.document_type, pattern.field_name, pattern.pattern,
                 pattern.label, pattern.confidence, pattern.hit_count, pattern.miss_count,
                 pattern.last_hit_at, pattern.created_at, pattern.updated_at,
                 pattern.source, int(pattern.active), json.dumps(pattern.metadata)),
            )
            await db.commit()
        finally:
            await db.close()
        return pattern

    async def get(self, pattern_id: str) -> StoredPattern | None:
        db = await self._get_db()
        try:
            cursor = await db.execute("SELECT * FROM patterns WHERE id = ?", (pattern_id,))
            row = await cursor.fetchone()
            return self._row_to_pattern(row) if row else None
        finally:
            await db.close()

    async def get_by_type(self, document_type: str, active_only: bool = True) -> list[StoredPattern]:
        db = await self._get_db()
        try:
            query = "SELECT * FROM patterns WHERE document_type = ?"
            params: list = [document_type]
            if active_only:
                query += " AND active = 1"
            query += " ORDER BY confidence DESC, hit_count DESC"
            cursor = await db.execute(query, params)
            rows = await cursor.fetchall()
            return [self._row_to_pattern(r) for r in rows]
        finally:
            await db.close()

    async def get_all(self, active_only: bool = True) -> list[StoredPattern]:
        db = await self._get_db()
        try:
            query = "SELECT * FROM patterns"
            if active_only:
                query += " WHERE active = 1"
            query += " ORDER BY document_type, field_name, confidence DESC"
            cursor = await db.execute(query)
            rows = await cursor.fetchall()
            return [self._row_to_pattern(r) for r in rows]
        finally:
            await db.close()

    async def update(self, pattern: StoredPattern) -> StoredPattern:
        pattern.updated_at = datetime.now(timezone.utc).isoformat()
        db = await self._get_db()
        try:
            await db.execute(
                """UPDATE patterns SET document_type=?, field_name=?, pattern=?, label=?,
                   confidence=?, hit_count=?, miss_count=?, last_hit_at=?, updated_at=?,
                   source=?, active=?, metadata=? WHERE id=?""",
                (pattern.document_type, pattern.field_name, pattern.pattern, pattern.label,
                 pattern.confidence, pattern.hit_count, pattern.miss_count, pattern.last_hit_at,
                 pattern.updated_at, pattern.source, int(pattern.active),
                 json.dumps(pattern.metadata), pattern.id),
            )
            await db.commit()
        finally:
            await db.close()
        return pattern

    async def delete(self, pattern_id: str) -> bool:
        db = await self._get_db()
        try:
            cursor = await db.execute("DELETE FROM patterns WHERE id = ?", (pattern_id,))
            await db.commit()
            return cursor.rowcount > 0
        finally:
            await db.close()

    async def record_hit(self, pattern_id: str) -> None:
        now = datetime.now(timezone.utc).isoformat()
        db = await self._get_db()
        try:
            await db.execute(
                "UPDATE patterns SET hit_count = hit_count + 1, last_hit_at = ? WHERE id = ?",
                (now, pattern_id),
            )
            await db.commit()
        finally:
            await db.close()

    async def record_miss(self, pattern_id: str) -> None:
        db = await self._get_db()
        try:
            await db.execute(
                "UPDATE patterns SET miss_count = miss_count + 1 WHERE id = ?",
                (pattern_id,),
            )
            await db.commit()
        finally:
            await db.close()

    async def get_analytics(self) -> PatternAnalytics:
        db = await self._get_db()
        try:
            cursor = await db.execute("SELECT * FROM patterns")
            rows = await cursor.fetchall()
            all_patterns = [self._row_to_pattern(r) for r in rows]
        finally:
            await db.close()

        active = [p for p in all_patterns if p.active]
        rates = [p.success_rate for p in active if p.total_attempts > 0]

        by_type: dict[str, int] = {}
        by_source: dict[str, int] = {}
        for p in active:
            by_type[p.document_type] = by_type.get(p.document_type, 0) + 1
            by_source[p.source] = by_source.get(p.source, 0) + 1

        top = sorted(active, key=lambda p: p.hit_count, reverse=True)[:10]
        low = [p for p in active if p.total_attempts >= 10 and p.success_rate < 0.5]

        return PatternAnalytics(
            total_patterns=len(all_patterns),
            active_patterns=len(active),
            total_hits=sum(p.hit_count for p in all_patterns),
            total_misses=sum(p.miss_count for p in all_patterns),
            avg_success_rate=round(sum(rates) / len(rates), 3) if rates else 0.0,
            top_patterns=[p.to_dict() for p in top],
            patterns_by_type=by_type,
            patterns_by_source=by_source,
            low_performing=[p.to_dict() for p in low],
        )


async def seed_default_patterns(repo: PatternRepository) -> int:
    """Seed the database with the hardcoded pattern library. Returns count of patterns added."""
    from app.services.pattern_engine import DOCUMENT_PATTERNS

    existing = await repo.get_all(active_only=False)
    if existing:
        return 0

    count = 0
    for doc_type, fields in DOCUMENT_PATTERNS.items():
        for field_name, patterns in fields.items():
            for pat_def in patterns:
                pattern = StoredPattern(
                    id=str(uuid.uuid4()),
                    document_type=doc_type,
                    field_name=pat_def.get("field", field_name),
                    pattern=pat_def["pattern"],
                    label=pat_def.get("label"),
                    confidence=pat_def["confidence"],
                    source="seeded",
                )
                await repo.save(pattern)
                count += 1

    logger.info("Seeded %d default patterns into pattern store", count)
    return count
