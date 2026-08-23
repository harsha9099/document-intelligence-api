from typing import Any

from pydantic import BaseModel


class DocumentQuality(BaseModel):
    readable: bool
    issues: list[str] = []


class DocumentValidation(BaseModel):
    is_expired: bool | None = None
    expiry_date: str | None = None
    issues: list[str] = []


class DocumentResponse(BaseModel):
    document_type: str
    document_subtype: str | None = None
    title: str
    confidence: float
    quality: DocumentQuality | None = None
    content: dict[str, Any]
    validation: DocumentValidation | None = None
    raw_text: str | None = None


class StoredDocument(DocumentResponse):
    id: str
    filename: str
    file_size_bytes: int = 0
    file_content_type: str = ""
    storage_path: str | None = None
    uploaded_at: str = ""
    processed_at: str = ""
    processing_duration_ms: int = 0
    provider_used: str = ""
    model_used: str | None = None
    page_count: int | None = None
    extraction_metadata: dict[str, Any] = {}


class ErrorResponse(BaseModel):
    error: str
    detail: str | None = None
