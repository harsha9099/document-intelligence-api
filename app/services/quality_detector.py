from enum import Enum


class DocumentQualityTier(str, Enum):
    DIGITAL_PDF = "digital_pdf"
    SCANNED_PDF = "scanned_pdf"
    PHOTO = "photo"


_IMAGE_EXTENSIONS = {"jpg", "jpeg", "png", "tiff", "bmp", "webp"}
_DIGITAL_PDF_TEXT_THRESHOLD = 500


def detect_quality(file_bytes: bytes, filename: str, extracted_text: str | None) -> DocumentQualityTier:
    ext = filename.rsplit(".", 1)[-1].lower() if "." in filename else ""

    if ext in _IMAGE_EXTENSIONS:
        return DocumentQualityTier.PHOTO

    if ext == "pdf":
        text = extracted_text or ""
        if len(text.strip()) >= _DIGITAL_PDF_TEXT_THRESHOLD:
            return DocumentQualityTier.DIGITAL_PDF
        return DocumentQualityTier.SCANNED_PDF

    return DocumentQualityTier.PHOTO
