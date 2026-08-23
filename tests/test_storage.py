import pytest

from app.storage.local_storage import LocalFileStorage


@pytest.fixture
def storage(tmp_path):
    return LocalFileStorage(base_path=str(tmp_path))


async def test_save_creates_file_and_returns_path(storage, tmp_path):
    result = await storage.save("file-001", "document.pdf", b"%PDF-test-content")

    assert result is not None
    assert "file-001" in result
    assert "document.pdf" in result

    # Verify the file actually exists on disk
    from pathlib import Path
    assert Path(result).exists()
    assert Path(result).read_bytes() == b"%PDF-test-content"


async def test_get_retrieves_bytes(storage):
    content = b"hello bytes"
    await storage.save("file-002", "test.txt", content)

    retrieved = await storage.get("file-002")

    assert retrieved == content


async def test_get_nonexistent_id_returns_none(storage):
    result = await storage.get("does-not-exist")
    assert result is None


async def test_delete_removes_file_and_returns_true(storage, tmp_path):
    await storage.save("file-003", "to_delete.pdf", b"data")

    result = await storage.delete("file-003")

    assert result is True
    # Directory should be gone
    assert not (tmp_path / "file-003").exists()


async def test_delete_nonexistent_returns_false(storage):
    result = await storage.delete("never-existed")
    assert result is False


async def test_save_overwrites_existing_file(storage, tmp_path):
    await storage.save("file-004", "doc.pdf", b"original")
    await storage.save("file-004", "doc.pdf", b"updated")

    retrieved = await storage.get("file-004")
    assert retrieved == b"updated"


async def test_get_filename_returns_correct_name(storage):
    await storage.save("file-005", "report.pdf", b"content")
    filename = storage.get_filename("file-005")
    assert filename == "report.pdf"


async def test_get_filename_nonexistent_returns_none(storage):
    filename = storage.get_filename("no-such-id")
    assert filename is None


async def test_save_and_get_binary_content(storage):
    binary_data = bytes(range(256))
    await storage.save("file-006", "binary.bin", binary_data)

    retrieved = await storage.get("file-006")
    assert retrieved == binary_data


async def test_delete_then_get_returns_none(storage):
    await storage.save("file-007", "gone.pdf", b"temporary")
    await storage.delete("file-007")

    result = await storage.get("file-007")
    assert result is None
