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


class ErrorResponse(BaseModel):
    error: str
    detail: str | None = None
