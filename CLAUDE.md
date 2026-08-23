# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A FICA/KYC document intelligence API that accepts PDF or image uploads (identity documents, proof of address, bank statements, payslips) and returns structured JSON extraction powered by LLM vision. Two implementations exist side-by-side: a primary **Python/FastAPI** service and a parallel **.NET 9** port.

## Build & Run Commands

### Python (primary)

```bash
# Setup
python -m venv .venv && source .venv/bin/activate
pip install -r requirements-dev.txt

# Run dev server
uvicorn app.main:app --reload --port 8000

# Run all tests
pytest

# Run a single test
pytest tests/test_api.py::test_health

# Lint
ruff check .

# Format
ruff format .
```

### .NET port (`dotnet/DocumentIntelligence.Api/`)

```bash
cd dotnet/DocumentIntelligence.Api
dotnet build
dotnet run
```

## Architecture

```
app/
├── main.py              # FastAPI app, /extract and /extract/batch endpoints
├── config.py            # pydantic-settings (reads .env)
├── services/
│   └── document_service.py  # Orchestrates extraction pipeline
├── extractors/
│   ├── pdf_extractor.py     # PyMuPDF: text extraction + page-to-image rendering
│   └── image_extractor.py   # Tesseract OCR + image prep for LLM
└── llm/
    ├── base.py              # Abstract LLMProvider + shared system prompt
    ├── factory.py           # Provider registry (anthropic, openai)
    ├── anthropic_provider.py
    └── openai_provider.py
```

### Request Flow

1. File upload hits `/extract` (single) or `/extract/batch` (up to 10 files)
2. `document_service.process_document` determines extraction strategy:
   - PDFs: extract text via PyMuPDF; render pages as images for vision
   - Images: attempt Tesseract OCR; always send original to LLM vision
   - Fallback: if text is insufficient (<100 chars), force vision path
3. LLM provider receives text + images with a structured system prompt that defines per-document-type JSON schemas
4. Response is parsed into `DocumentResponse` (type, confidence, structured content, quality, validation)

### LLM Providers

Both providers implement `LLMProvider.analyze_document(text, images, extraction_hint)`. The system prompt (in `base.py`) defines exact JSON output schemas per document type. Provider selection is per-request via form field or falls back to `DEFAULT_LLM_PROVIDER` env var.

## Configuration

Copy `.env.example` to `.env`. Key settings:
- `ANTHROPIC_API_KEY` / `OPENAI_API_KEY` — at least one required
- `DEFAULT_LLM_PROVIDER` — `anthropic` or `openai`
- `MAX_FILE_SIZE_MB` — upload size limit (default 50)
- `ALLOWED_EXTENSIONS` — comma-separated list

## Testing

Tests use `httpx.AsyncClient` with ASGI transport (no server needed). LLM calls are mocked. `pytest-asyncio` is configured with `asyncio_mode = "auto"`.

## External Dependencies

- **Tesseract** must be installed on the host (`brew install tesseract` / `apt install tesseract-ocr`)
- **PyMuPDF** handles PDF parsing (no external binary needed)
