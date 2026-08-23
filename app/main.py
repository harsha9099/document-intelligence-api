import logging
from enum import Enum

from fastapi import FastAPI, File, Form, HTTPException, Query, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response

from app.config import settings
from app.llm.factory import get_llm_provider
from app.logging_config import configure_logging
from app.middleware import CorrelationIdMiddleware, request_id_var
from app.models.schemas import ErrorResponse, StoredDocument
from app.repositories import create_repository
from app.services.extraction_pipeline import ExtractionPipeline
from app.repositories.pattern_store import (
    InMemoryPatternRepository,
    PatternRepository,
    SqlitePatternRepository,
    StoredPattern,
    seed_default_patterns,
)
from app.storage import create_storage

configure_logging()
logger = logging.getLogger(__name__)

app = FastAPI(
    title="FICA Document Intelligence API",
    description="Upload FICA/KYC documents and get structured JSON extraction powered by LLM vision.",
    version="0.1.0",
)

app.add_middleware(CorrelationIdMiddleware)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_repository = create_repository()
_storage = create_storage()
_pattern_store: PatternRepository = (
    SqlitePatternRepository("patterns.db")
    if settings.persistence_backend == "sqlite"
    else InMemoryPatternRepository()
)


class DocumentTypeHint(str, Enum):
    auto = "auto"
    identity_document = "identity_document"
    proof_of_address = "proof_of_address"
    bank_statement = "bank_statement"
    payslip = "payslip"
    invoice = "invoice"
    bill = "bill"


@app.get("/health")
async def health():
    return {"status": "healthy"}


@app.get("/documents", response_model=list[StoredDocument])
async def list_documents(
    type: str | None = Query(default=None, description="Filter by document type"),
    limit: int = Query(default=100, ge=1, le=500),
    offset: int = Query(default=0, ge=0),
):
    return await _repository.list_all(limit=limit, offset=offset, document_type=type)


@app.get("/documents/{doc_id}", response_model=StoredDocument)
async def get_document(doc_id: str):
    doc = await _repository.get(doc_id)
    if not doc:
        raise HTTPException(status_code=404, detail=f"Document {doc_id} not found")
    return doc


@app.get("/documents/{doc_id}/file", summary="Download the original uploaded file")
async def get_document_file(doc_id: str):
    doc = await _repository.get(doc_id)
    if not doc:
        raise HTTPException(status_code=404, detail=f"Document {doc_id} not found")
    file_bytes = await _storage.get(doc_id)
    if file_bytes is None:
        raise HTTPException(status_code=404, detail="Original file not found in storage")
    return Response(
        content=file_bytes,
        media_type=doc.file_content_type or "application/octet-stream",
        headers={"Content-Disposition": f'attachment; filename="{doc.filename}"'},
    )


@app.delete("/documents/{doc_id}", status_code=204)
async def delete_document(doc_id: str):
    deleted = await _repository.delete(doc_id)
    if not deleted:
        raise HTTPException(status_code=404, detail=f"Document {doc_id} not found")
    await _storage.delete(doc_id)


async def _handle_extract(
    file: UploadFile,
    provider: str | None,
    model: str | None,
    hint: str | None,
    use_vision: bool,
    type_hint: str | None = None,
) -> StoredDocument:
    request_id = request_id_var.get("-")

    if not file.filename:
        raise HTTPException(status_code=400, detail="No filename provided")

    ext = file.filename.rsplit(".", 1)[-1].lower() if "." in file.filename else ""
    if ext not in settings.allowed_extensions_list:
        raise HTTPException(
            status_code=400,
            detail=f"File type '.{ext}' not supported. Allowed: {settings.allowed_extensions_list}",
        )

    file_bytes = await file.read()
    if len(file_bytes) > settings.max_file_size_bytes:
        raise HTTPException(
            status_code=400,
            detail=f"File exceeds maximum size of {settings.max_file_size_mb}MB",
        )

    kwargs: dict = {}
    if model:
        kwargs["model"] = model

    try:
        llm = get_llm_provider(provider, **kwargs)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    logger.info(
        "provider_selected",
        extra={"request_id": request_id, "provider": llm.__class__.__name__, "filename": file.filename, "strategy": settings.extraction_strategy},
    )

    extraction_hint = type_hint or ""
    if hint:
        extraction_hint = f"{extraction_hint} {hint}".strip()

    pipeline = ExtractionPipeline(provider=llm, file_storage=_storage, pattern_store=_pattern_store)

    try:
        stored = await pipeline.extract(
            file_bytes=file_bytes,
            filename=file.filename,
            hint=extraction_hint or None,
            use_vision=use_vision,
            file_content_type=file.content_type or "application/octet-stream",
        )
    except Exception as e:
        logger.error(
            "extraction_failed",
            extra={"request_id": request_id, "filename": file.filename, "error": str(e)},
            exc_info=True,
        )
        raise HTTPException(status_code=422, detail=f"Document processing failed: {e}")

    await _repository.save(stored)
    logger.info(
        "extraction_complete",
        extra={
            "request_id": request_id,
            "doc_id": stored.id,
            "document_type": stored.document_type,
            "confidence": stored.confidence,
            "duration_ms": stored.processing_duration_ms,
            "tier": stored.extraction_metadata.get("tier"),
            "llm_skipped": stored.extraction_metadata.get("llm_skipped", False),
        },
    )
    return stored


@app.post("/extract", response_model=StoredDocument, tags=["Extraction"],
          summary="Auto-detect document type and extract structured data",
          responses={400: {"model": ErrorResponse}, 422: {"model": ErrorResponse}})
async def extract_document(
    file: UploadFile = File(...),
    document_type: DocumentTypeHint = Form(default=DocumentTypeHint.auto),
    provider: str | None = Form(default=None),
    model: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    type_hint = None
    if document_type != DocumentTypeHint.auto:
        type_hint = f"This document is expected to be a {document_type.value.replace('_', ' ')}."
    return await _handle_extract(file, provider, model, hint, use_vision, type_hint)


@app.post("/extract/identity", response_model=StoredDocument, tags=["Extraction"],
          summary="Extract from identity documents: passport, national ID, driver's license, asylum permit")
async def extract_identity(
    file: UploadFile = File(...),
    provider: str | None = Form(default=None),
    model: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    return await _handle_extract(file, provider, model, hint, use_vision,
        "This is an identity document (passport, national ID, driver's license, or similar).")


@app.post("/extract/bank-statement", response_model=StoredDocument, tags=["Extraction"],
          summary="Extract from bank statements: current account, savings, credit card, loan")
async def extract_bank_statement(
    file: UploadFile = File(...),
    provider: str | None = Form(default=None),
    model: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    return await _handle_extract(file, provider, model, hint, use_vision,
        "This is a bank statement (current account, savings, credit card, or loan statement).")


@app.post("/extract/proof-of-address", response_model=StoredDocument, tags=["Extraction"],
          summary="Extract from proof of address: utility bill, municipal account, lease, bank letter")
async def extract_proof_of_address(
    file: UploadFile = File(...),
    provider: str | None = Form(default=None),
    model: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    return await _handle_extract(file, provider, model, hint, use_vision,
        "This is a proof of address document (utility bill, municipal account, lease agreement, bank letter, or similar).")


@app.post("/extract/payslip", response_model=StoredDocument, tags=["Extraction"],
          summary="Extract from payslips: monthly payslip, annual tax certificate, employment letter")
async def extract_payslip(
    file: UploadFile = File(...),
    provider: str | None = Form(default=None),
    model: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    return await _handle_extract(file, provider, model, hint, use_vision,
        "This is a payslip or employment income document (monthly payslip, annual tax certificate, or employment letter).")


@app.post("/extract/invoice", response_model=StoredDocument, tags=["Extraction"],
          summary="Extract from invoices: commercial, proforma, tax invoice")
async def extract_invoice(
    file: UploadFile = File(...),
    provider: str | None = Form(default=None),
    model: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    return await _handle_extract(file, provider, model, hint, use_vision,
        "This is an invoice (commercial invoice, proforma invoice, or tax invoice). Extract line items, totals, and payment details.")


@app.post("/extract/bill", response_model=StoredDocument, tags=["Extraction"],
          summary="Extract from bills: phone, medical, subscription, other recurring bills")
async def extract_bill(
    file: UploadFile = File(...),
    provider: str | None = Form(default=None),
    model: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    return await _handle_extract(file, provider, model, hint, use_vision,
        "This is a bill (phone bill, medical bill, subscription, or similar recurring charge document).")


@app.post("/extract/batch", response_model=list[StoredDocument])
async def extract_batch(
    files: list[UploadFile] = File(..., description="Multiple FICA document files"),
    document_type: DocumentTypeHint = Form(default=DocumentTypeHint.auto),
    provider: str | None = Form(default=None),
    hint: str | None = Form(default=None),
    use_vision: bool = Form(default=True),
):
    request_id = request_id_var.get("-")

    if len(files) > 10:
        raise HTTPException(status_code=400, detail="Maximum 10 files per batch request")

    type_hint = None
    if document_type != DocumentTypeHint.auto:
        type_hint = f"This document is expected to be a {document_type.value.replace('_', ' ')}."

    results = []
    for f in files:
        try:
            stored = await _handle_extract(f, provider, None, hint, use_vision, type_hint)
            results.append(stored)
        except HTTPException as e:
            logger.warning(
                "batch_file_skipped",
                extra={"request_id": request_id, "filename": f.filename, "reason": e.detail},
            )
        except Exception as e:
            logger.error(
                "batch_file_failed",
                extra={"request_id": request_id, "filename": f.filename, "error": str(e)},
            )

    logger.info(
        "batch_completed",
        extra={"request_id": request_id, "processed": len(results), "total": len(files)},
    )
    return results


# ── Pattern Management Endpoints ──────────────────────────────────────────────


@app.on_event("startup")
async def _seed_patterns():
    count = await seed_default_patterns(_pattern_store)
    if count:
        logger.info("Seeded %d default patterns on first startup", count)


@app.get("/patterns", tags=["Patterns"], summary="List all stored patterns")
async def list_patterns(
    document_type: str | None = Query(default=None),
    active_only: bool = Query(default=True),
):
    if document_type:
        return [p.to_dict() for p in await _pattern_store.get_by_type(document_type, active_only)]
    return [p.to_dict() for p in await _pattern_store.get_all(active_only)]


@app.get("/patterns/analytics", tags=["Patterns"], summary="Pattern performance analytics")
async def pattern_analytics():
    analytics = await _pattern_store.get_analytics()
    return {
        "total_patterns": analytics.total_patterns,
        "active_patterns": analytics.active_patterns,
        "total_hits": analytics.total_hits,
        "total_misses": analytics.total_misses,
        "avg_success_rate": analytics.avg_success_rate,
        "patterns_by_type": analytics.patterns_by_type,
        "patterns_by_source": analytics.patterns_by_source,
        "top_patterns": analytics.top_patterns,
        "low_performing": analytics.low_performing,
    }


@app.get("/patterns/{pattern_id}", tags=["Patterns"], summary="Get a specific pattern")
async def get_pattern(pattern_id: str):
    pattern = await _pattern_store.get(pattern_id)
    if not pattern:
        raise HTTPException(status_code=404, detail=f"Pattern {pattern_id} not found")
    return pattern.to_dict()


@app.post("/patterns", tags=["Patterns"], status_code=201, summary="Add a new pattern")
async def create_pattern(
    document_type: str = Form(..., description="e.g. identity_document, invoice, bill"),
    field_name: str = Form(..., description="Field this pattern extracts, e.g. id_number, total_amount"),
    pattern: str = Form(..., description="Regex pattern with capture group"),
    label: str | None = Form(default=None, description="Human label, e.g. 'sa_national_id'"),
    confidence: float = Form(default=0.8, ge=0.0, le=1.0),
):
    import re
    try:
        re.compile(pattern)
    except re.error as e:
        raise HTTPException(status_code=400, detail=f"Invalid regex: {e}")

    stored = StoredPattern(
        id="",
        document_type=document_type,
        field_name=field_name,
        pattern=pattern,
        label=label,
        confidence=confidence,
        source="manual",
    )
    saved = await _pattern_store.save(stored)
    return saved.to_dict()


@app.put("/patterns/{pattern_id}", tags=["Patterns"], summary="Update an existing pattern")
async def update_pattern(
    pattern_id: str,
    pattern: str | None = Form(default=None),
    confidence: float | None = Form(default=None),
    active: bool | None = Form(default=None),
    label: str | None = Form(default=None),
):
    existing = await _pattern_store.get(pattern_id)
    if not existing:
        raise HTTPException(status_code=404, detail=f"Pattern {pattern_id} not found")

    if pattern is not None:
        import re
        try:
            re.compile(pattern)
        except re.error as e:
            raise HTTPException(status_code=400, detail=f"Invalid regex: {e}")
        existing.pattern = pattern
    if confidence is not None:
        existing.confidence = confidence
    if active is not None:
        existing.active = active
    if label is not None:
        existing.label = label

    updated = await _pattern_store.update(existing)
    return updated.to_dict()


@app.delete("/patterns/{pattern_id}", tags=["Patterns"], status_code=204, summary="Delete a pattern")
async def delete_pattern(pattern_id: str):
    deleted = await _pattern_store.delete(pattern_id)
    if not deleted:
        raise HTTPException(status_code=404, detail=f"Pattern {pattern_id} not found")


@app.post("/patterns/seed", tags=["Patterns"], summary="Re-seed default patterns (safe — skips if patterns exist)")
async def seed_patterns():
    count = await seed_default_patterns(_pattern_store)
    return {"seeded": count, "message": f"Added {count} patterns" if count else "Patterns already exist, skipped"}


@app.post("/patterns/test", tags=["Patterns"], summary="Test patterns against sample text")
async def test_patterns(
    text: str = Form(..., description="Sample text to test patterns against"),
    document_type: str | None = Form(default=None),
):
    from app.services.pattern_engine import extract_with_patterns

    stored = None
    if document_type:
        stored = await _pattern_store.get_by_type(document_type)

    result = extract_with_patterns(text, document_type, stored)
    return {
        "detected_type": result.detected_type,
        "type_confidence": result.type_confidence,
        "fields": result.fields,
        "field_confidences": result.field_confidences,
        "overall_confidence": result.overall_confidence,
        "patterns_matched": result.patterns_matched,
        "patterns_attempted": result.patterns_attempted,
        "matched_pattern_ids": result.matched_pattern_ids,
    }
