from typing import Any

from app.extractors.image_extractor import extract_text_from_image, prepare_image_for_llm
from app.extractors.pdf_extractor import extract_images_from_pdf, extract_text_from_pdf
from app.llm.base import LLMProvider
from app.models.schemas import DocumentQuality, DocumentResponse, DocumentValidation


async def process_document(
    file_bytes: bytes,
    filename: str,
    provider: LLMProvider,
    extraction_hint: str | None = None,
    use_vision: bool = True,
) -> DocumentResponse:
    ext = filename.rsplit(".", 1)[-1].lower() if "." in filename else ""
    text: str | None = None
    images: list[bytes] | None = None

    if ext == "pdf":
        text = extract_text_from_pdf(file_bytes)
        if use_vision:
            images = extract_images_from_pdf(file_bytes)
        # If PDF has no extractable text (scanned), always use vision
        if not text.strip() and not images:
            images = extract_images_from_pdf(file_bytes)
    else:
        # For images (including photos of documents), vision is the primary path
        if use_vision:
            images = [prepare_image_for_llm(file_bytes)]
        # Also attempt OCR as supplementary text
        text = extract_text_from_image(file_bytes)

    # For FICA docs, always prefer sending the image to the LLM —
    # camera-captured docs, stamps, handwriting, and security features
    # are best handled by vision models.
    if not images and not (text and len(text.strip()) > 100):
        images = [prepare_image_for_llm(file_bytes)]

    result: dict[str, Any] = await provider.analyze_document(
        text=text if text and text.strip() else None,
        images=images,
        extraction_hint=extraction_hint,
    )

    quality_data = result.get("quality")
    quality = None
    if isinstance(quality_data, dict):
        quality = DocumentQuality(
            readable=quality_data.get("readable", True),
            issues=quality_data.get("issues", []),
        )

    validation_data = result.get("validation")
    validation = None
    if isinstance(validation_data, dict):
        validation = DocumentValidation(
            is_expired=validation_data.get("is_expired"),
            expiry_date=validation_data.get("expiry_date"),
            issues=validation_data.get("issues", []),
        )

    return DocumentResponse(
        document_type=result.get("document_type", "unknown"),
        document_subtype=result.get("document_subtype"),
        title=result.get("title", filename),
        confidence=result.get("confidence", 0.0),
        quality=quality,
        content=result.get("content", {}),
        validation=validation,
        raw_text=text if text and text.strip() else None,
    )
