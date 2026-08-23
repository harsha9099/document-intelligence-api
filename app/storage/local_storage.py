import asyncio
import shutil
from pathlib import Path

from app.storage.base import FileStorage


class LocalFileStorage(FileStorage):
    def __init__(self, base_path: str = "./uploads") -> None:
        self._base = Path(base_path)

    async def save(self, file_id: str, filename: str, file_bytes: bytes) -> str:
        dir_path = self._base / file_id
        dir_path.mkdir(parents=True, exist_ok=True)
        file_path = dir_path / filename
        await asyncio.to_thread(file_path.write_bytes, file_bytes)
        return str(file_path)

    async def get(self, file_id: str) -> bytes | None:
        dir_path = self._base / file_id
        if not dir_path.exists():
            return None
        files = list(dir_path.iterdir())
        if not files:
            return None
        return await asyncio.to_thread(files[0].read_bytes)

    async def delete(self, file_id: str) -> bool:
        dir_path = self._base / file_id
        if not dir_path.exists():
            return False
        await asyncio.to_thread(shutil.rmtree, dir_path)
        return True

    def get_filename(self, file_id: str) -> str | None:
        dir_path = self._base / file_id
        if not dir_path.exists():
            return None
        files = list(dir_path.iterdir())
        return files[0].name if files else None
