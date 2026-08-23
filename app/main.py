import logging
from enum import Enum

from fastapi import FastAPI, File, Form, HTTPException, Query, UploadFile
from fastapi.middleware.cors import CORSMiddleware

from app.config import settings
from app.llm.factory import get_llm_provider
from app.logging_config import configure_logging
from app.middleware import CorrelationIdMiddleware, request_id_var
from app.models.schemas import DocumentResponse, ErrorResponse, StoredDocument
from app.repositories import create_repository
from app.services.document_service import process_document

configure_logging()
logger = logging.getLogger(__name__)

app = FastAPI(
    title="FICA Document Intelligence API",
    description="Upload FICA/KYC documents (ID, proof of address, bank statements, payslips) as PDF or image and get structured JSON extraction powered by LLM vision.",
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


@app.delete("/documents/{doc_id}", status_code=204)
async def delete_document(doc_id: str):
    deleted = await _repository.delete(doc_id)
    if not deleted:
        raise HTTPException(status_code=404, detail=f"Document {doc_id} not found")


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
    kwargs["filename_hint"] = file.filename

    try:
        llm = get_llm_provider(provider, **kwargs)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    logger.info(
        "provider_selected",
        extra={"request_id": request_id, "provider": llm.__class__.__name__, "filename": file.filename},
    )

    extraction_hint = type_hint or ""
    if hint:
        extraction_hint = f"{extraction_hint} {hint}".strip()

    try:
        result = await process_document(
            file_bytes=file_bytes,
            filename=file.filename,
            provider=llm,
            extraction_hint=extraction_hint or None,
            use_vision=use_vision,
        )
    except Exception as e:
        logger.error(
            "extraction_failed",
            extra={"request_id": request_id, "filename": file.filename, "error": str(e)},
            exc_info=True,
        )
        raise HTTPException(status_code=422, detail=f"Document processing failed: {e}")

    return await _repository.save(result, file.filename)


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


@app.post(
    "/extract/batch",
    response_model=list[StoredDocument],
)
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

    try:
        llm = get_llm_provider(provider)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    logger.info(
        "batch_started",
        extra={"request_id": request_id, "file_count": len(files), "provider": llm.__class__.__name__},
    )

    extraction_hint = hint or ""
    if document_type != DocumentTypeHint.auto:
        type_context = f"This document is expected to be a {document_type.value.replace('_', ' ')}."
        extraction_hint = f"{type_context} {extraction_hint}".strip()

    results = []
    for f in files:
        file_bytes = await f.read()
        if len(file_bytes) > settings.max_file_size_bytes:
            logger.warning(
                "batch_file_skipped",
                extra={"request_id": request_id, "filename": f.filename, "reason": "file_too_large"},
            )
            continue

        try:
            result = await process_document(
                file_bytes=file_bytes,
                filename=f.filename or "unknown",
                provider=llm,
                extraction_hint=extraction_hint or None,
                use_vision=use_vision,
            )
            stored = await _repository.save(result, f.filename or "unknown")
            results.append(stored)
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
