import io

from PIL import Image


def extract_text_from_image(file_bytes: bytes) -> str:
    try:
        import pytesseract

        image = Image.open(io.BytesIO(file_bytes))
        return pytesseract.image_to_string(image)
    except Exception:
        return ""


def prepare_image_for_llm(file_bytes: bytes) -> bytes:
    image = Image.open(io.BytesIO(file_bytes))
    if image.mode == "RGBA":
        image = image.convert("RGB")
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    return buf.getvalue()
