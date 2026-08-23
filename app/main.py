import logging
from enum import Enum

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware

from app.config import settings
from app.llm.factory import get_llm_provider
from app.logging_config import configure_logging
from app.middleware import CorrelationIdMiddleware, request_id_var
from app.models.schemas import DocumentResponse, ErrorResponse, StoredDocument
from app.repositories.document_repository import InMemoryDocumentRepository
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

_repository = InMemoryDocumentRepository()


class DocumentTypeHint(str, Enum):
    auto = "auto"
    identity_document = "identity_document"
    proof_of_address = "proof_of_address"
    bank_statement = "bank_statement"
    payslip = "payslip"


@app.get("/health")
async def health():
    return {"status": "healthy"}


@app.get("/documents", response_model=list[StoredDocument])
async def list_documents():
    return _repository.list_all()


@app.get("/documents/{doc_id}", response_model=StoredDocument)
async def get_document(doc_id: str):
    doc = _repository.get(doc_id)
    if not doc:
        raise HTTPException(status_code=404, detail=f"Document {doc_id} not found")
    return doc


@app.post(
    "/extract",
    response_model=StoredDocument,
    responses={400: {"model": ErrorResponse}, 422: {"model": ErrorResponse}},
)
async def extract_document(
    file: UploadFile = File(..., description="FICA document (PDF, photo, scan)"),
    document_type: DocumentTypeHint = Form(
        default=DocumentTypeHint.auto,
        description="Hint the expected document type for better extraction accuracy",
    ),
    provider: str | None = Form(default=None, description="LLM provider: anthropic, aitrium, bedrock, openai, mock"),
    model: str | None = Form(default=None, description="Model override"),
    hint: str | None = Form(default=None, description="Additional extraction guidance"),
    use_vision: bool = Form(default=True, description="Use LLM vision for visual analysis"),
):
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
    # Pass filename as hint so mock provider can return the right doc type
    kwargs["filename_hint"] = file.filename

    try:
        llm = get_llm_provider(provider, **kwargs)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    logger.info(
        "provider_selected",
        extra={"request_id": request_id, "provider": llm.__class__.__name__, "filename": file.filename},
    )

    extraction_hint = hint or ""
    if document_type != DocumentTypeHint.auto:
        type_context = f"This document is expected to be a {document_type.value.replace('_', ' ')}."
        extraction_hint = f"{type_context} {extraction_hint}".strip()

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

    stored = _repository.save(result, file.filename)
    return stored


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
            stored = _repository.save(result, f.filename or "unknown")
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
