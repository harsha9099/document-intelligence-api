import logging
import time
import uuid
from datetime import datetime, timezone
from typing import Any

from app.config import settings
from app.extractors.pdf_extractor import get_page_count
from app.llm.base import LLMProvider
from app.models.schemas import DocumentQuality, DocumentResponse, DocumentValidation, StoredDocument
from app.services.document_service import process_document

logger = logging.getLogger(__name__)


class ExtractionPipeline:
    def __init__(self, provider: LLMProvider, file_storage=None):
        self._provider = provider
        self._storage = file_storage

    async def extract(
        self,
        file_bytes: bytes,
        filename: str,
        hint: str | None = None,
        use_vision: bool = True,
        document_id: str | None = None,
        file_content_type: str = "application/octet-stream",
    ) -> StoredDocument:
        doc_id = document_id or str(uuid.uuid4())
        uploaded_at = datetime.now(timezone.utc).isoformat()
        ext = filename.rsplit(".", 1)[-1].lower() if "." in filename else ""
        page_count = get_page_count(file_bytes) if ext == "pdf" else None

        storage_path: str | None = None
        if self._storage:
            storage_path = await self._storage.save(doc_id, filename, file_bytes)

        strategy = settings.extraction_strategy
        start = time.monotonic()

        result, metadata = await self._run_strategy(
            strategy, file_bytes, filename, hint, use_vision, ext
        )

        duration_ms = round((time.monotonic() - start) * 1000)
        processed_at = datetime.now(timezone.utc).isoformat()

        metadata["strategy_used"] = strategy

        return StoredDocument(
            id=doc_id,
            filename=filename,
            file_size_bytes=len(file_bytes),
            file_content_type=file_content_type,
            storage_path=storage_path,
            uploaded_at=uploaded_at,
            processed_at=processed_at,
            processing_duration_ms=duration_ms,
            provider_used=self._provider.__class__.__name__,
            model_used=getattr(self._provider, "model", None),
            page_count=page_count,
            document_type=result.document_type,
            document_subtype=result.document_subtype,
            title=result.title,
            confidence=result.confidence,
            quality=result.quality,
            content=result.content,
            validation=result.validation,
            raw_text=result.raw_text,
            extraction_metadata=metadata,
        )

    async def _run_strategy(
        self,
        strategy: str,
        file_bytes: bytes,
        filename: str,
        hint: str | None,
        use_vision: bool,
        ext: str,
    ) -> tuple[DocumentResponse, dict]:

        if strategy == "llm_only":
            result = await process_document(file_bytes, filename, self._provider, hint, use_vision)
            return result, {"tier": "llm", "llm_skipped": False}

        if strategy == "ocr_first":
            ocr_result = await self._ocr_extract(file_bytes, filename, ext)
            if ocr_result and ocr_result.confidence >= settings.confidence_threshold:
                logger.info("ocr_first: OCR confident enough, skipping LLM", extra={"filename": filename, "confidence": ocr_result.confidence})
                return ocr_result, {
                    "tier": "ocr",
                    "llm_skipped": True,
                    "tier1_confidence": ocr_result.confidence,
                    "estimated_cost_savings": "~90% vs LLM vision",
                }
            logger.info("ocr_first: OCR confidence too low, falling back to LLM", extra={"filename": filename})
            llm_result = await process_document(file_bytes, filename, self._provider, hint, use_vision)
            return llm_result, {
                "tier": "llm_fallback",
                "llm_skipped": False,
                "tier1_confidence": ocr_result.confidence if ocr_result else None,
                "tier2_confidence": llm_result.confidence,
            }

        if strategy == "azure_di_first":
            di_result = await self._azure_di_extract(file_bytes, filename, hint)
            if di_result and di_result.confidence >= settings.confidence_threshold:
                logger.info("azure_di_first: Azure DI confident, skipping LLM", extra={"filename": filename, "confidence": di_result.confidence})
                return di_result, {
                    "tier": "azure_di",
                    "llm_skipped": True,
                    "tier1_confidence": di_result.confidence,
                    "estimated_cost_savings": "~95% vs LLM vision",
                }
            logger.info("azure_di_first: Azure DI confidence too low, falling back to LLM", extra={"filename": filename})
            llm_result = await process_document(file_bytes, filename, self._provider, hint, use_vision)
            return llm_result, {
                "tier": "llm_fallback",
                "llm_skipped": False,
                "tier1_confidence": di_result.confidence if di_result else None,
                "tier2_confidence": llm_result.confidence,
            }

        if strategy == "hybrid":
            import asyncio
            di_task = asyncio.create_task(self._azure_di_extract(file_bytes, filename, hint))
            llm_task = asyncio.create_task(process_document(file_bytes, filename, self._provider, hint, use_vision))
            di_result, llm_result = await asyncio.gather(di_task, llm_task)
            merged, discrepancies = self._merge_results(di_result, llm_result)
            return merged, {
                "tier": "hybrid",
                "llm_skipped": False,
                "tier1_confidence": di_result.confidence if di_result else None,
                "tier2_confidence": llm_result.confidence,
                "discrepancies": discrepancies,
            }

        # fallback
        result = await process_document(file_bytes, filename, self._provider, hint, use_vision)
        return result, {"tier": "llm", "llm_skipped": False}

    async def _ocr_extract(self, file_bytes: bytes, filename: str, ext: str) -> DocumentResponse | None:
        try:
            from app.extractors.image_extractor import extract_text_from_image
            from app.extractors.pdf_extractor import extract_text_from_pdf

            text = extract_text_from_pdf(file_bytes) if ext == "pdf" else extract_text_from_image(file_bytes)
            if not text or len(text.strip()) < 100:
                return None

            return DocumentResponse(
                document_type="unknown",
                document_subtype=None,
                title=filename,
                confidence=0.6,
                quality=DocumentQuality(readable=True, issues=[]),
                content={"raw_text": text[:3000]},
                validation=None,
                raw_text=text,
            )
        except Exception as e:
            logger.warning("OCR extraction failed: %s", e)
            return None

    async def _azure_di_extract(self, file_bytes: bytes, filename: str, hint: str | None) -> DocumentResponse | None:
        try:
            from app.extractors import azure_di_extractor as di

            hint_lower = (hint or filename).lower()
            raw: dict | None = None

            if any(k in hint_lower for k in ("invoice",)):
                raw = await di.analyze_invoice(file_bytes)
            elif any(k in hint_lower for k in ("id", "passport", "identity", "license", "permit")):
                raw = await di.analyze_identity_document(file_bytes)
            elif any(k in hint_lower for k in ("receipt", "bill")):
                raw = await di.analyze_receipt(file_bytes)
            else:
                raw = await di.analyze_general(file_bytes)

            if not raw:
                return None

            quality_data = raw.get("quality") or {}
            validation_data = raw.get("validation") or {}
            return DocumentResponse(
                document_type=raw.get("document_type", "unknown"),
                document_subtype=raw.get("document_subtype"),
                title=raw.get("title", filename),
                confidence=raw.get("confidence", 0.0),
                quality=DocumentQuality(readable=quality_data.get("readable", True), issues=quality_data.get("issues", [])),
                content=raw.get("content", {}),
                validation=DocumentValidation(
                    is_expired=validation_data.get("is_expired"),
                    expiry_date=validation_data.get("expiry_date"),
                    issues=validation_data.get("issues", []),
                ),
                raw_text=None,
            )
        except Exception as e:
            logger.warning("Azure DI pipeline extraction failed: %s", e)
            return None

    def _merge_results(
        self, di_result: DocumentResponse | None, llm_result: DocumentResponse
    ) -> tuple[DocumentResponse, list[str]]:
        if not di_result:
            return llm_result, []

        discrepancies: list[str] = []

        if di_result.document_type != llm_result.document_type:
            discrepancies.append(f"document_type: azure_di={di_result.document_type}, llm={llm_result.document_type}")

        # Check shared content keys
        for key in set(di_result.content) & set(llm_result.content):
            v1, v2 = str(di_result.content.get(key)), str(llm_result.content.get(key))
            if v1 and v2 and v1 != v2 and v1 != "None" and v2 != "None":
                discrepancies.append(f"content.{key}: azure_di={v1}, llm={v2}")

        # Boost confidence if both tiers agree on document_type
        boosted_confidence = llm_result.confidence
        if not discrepancies or di_result.document_type == llm_result.document_type:
            boosted_confidence = min(1.0, llm_result.confidence * 1.05)

        merged = DocumentResponse(
            document_type=llm_result.document_type,
            document_subtype=llm_result.document_subtype,
            title=llm_result.title,
            confidence=round(boosted_confidence, 3),
            quality=llm_result.quality,
            content=llm_result.content,
            validation=llm_result.validation,
            raw_text=llm_result.raw_text,
        )
        return merged, discrepancies
