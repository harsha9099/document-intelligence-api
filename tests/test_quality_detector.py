import pytest

from app.services.quality_detector import DocumentQualityTier, detect_quality


def test_jpg_returns_photo():
    assert detect_quality(b"fake", "photo.jpg", None) == DocumentQualityTier.PHOTO


def test_png_returns_photo():
    assert detect_quality(b"fake", "doc.png", None) == DocumentQualityTier.PHOTO


def test_pdf_with_sufficient_text_returns_digital_pdf():
    assert detect_quality(b"%PDF", "doc.pdf", "x" * 600) == DocumentQualityTier.DIGITAL_PDF


def test_pdf_with_short_text_returns_scanned_pdf():
    assert detect_quality(b"%PDF", "doc.pdf", "short") == DocumentQualityTier.SCANNED_PDF


def test_pdf_with_no_text_returns_scanned_pdf():
    assert detect_quality(b"%PDF", "doc.pdf", None) == DocumentQualityTier.SCANNED_PDF


def test_pdf_with_text_exactly_at_threshold_returns_digital_pdf():
    # Threshold is 500 characters; exactly 500 should qualify
    assert detect_quality(b"%PDF", "doc.pdf", "x" * 500) == DocumentQualityTier.DIGITAL_PDF


def test_pdf_with_text_one_below_threshold_returns_scanned_pdf():
    assert detect_quality(b"%PDF", "doc.pdf", "x" * 499) == DocumentQualityTier.SCANNED_PDF


def test_pdf_with_whitespace_only_text_returns_scanned_pdf():
    # Strip is applied, so only-whitespace text counts as empty
    assert detect_quality(b"%PDF", "doc.pdf", "   ") == DocumentQualityTier.SCANNED_PDF


def test_unknown_extension_returns_photo():
    assert detect_quality(b"fake", "file.docx", None) == DocumentQualityTier.PHOTO


def test_no_extension_returns_photo():
    assert detect_quality(b"fake", "filewithoutextension", None) == DocumentQualityTier.PHOTO


def test_jpeg_extension_returns_photo():
    assert detect_quality(b"fake", "scan.jpeg", None) == DocumentQualityTier.PHOTO


def test_tiff_extension_returns_photo():
    assert detect_quality(b"fake", "scan.tiff", None) == DocumentQualityTier.PHOTO
