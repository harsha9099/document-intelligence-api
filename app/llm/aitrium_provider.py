import base64
import json
import logging
import time
from typing import Any

import anthropic

from app.config import settings
from app.llm.base import LLMProvider

logger = logging.getLogger(__name__)


class AitriumProvider(LLMProvider):
    def __init__(
        self,
        model: str | None = None,
        base_url: str | None = None,
        auth_token: str | None = None,
    ):
        self._base_url = base_url or settings.aitrium_base_url
        self._auth_token = auth_token or settings.aitrium_auth_token
        self.model = model or settings.aitrium_model

        if not self._base_url:
            raise ValueError("Aitrium base URL not configured")
        if not self._auth_token:
            raise ValueError("Aitrium auth token not configured")

        self.client = anthropic.AsyncAnthropic(
            api_key=self._auth_token,
            base_url=self._base_url.rstrip("/chat/completions").rstrip("/"),
        )

    async def analyze_document(
        self,
        text: str | None = None,
        images: list[bytes] | None = None,
        extraction_hint: str | None = None,
    ) -> dict[str, Any]:
        content = []

        if images:
            for img in images[:20]:
                b64 = base64.standard_b64encode(img).decode("utf-8")
                content.append(
                    {
                        "type": "image",
                        "source": {"type": "base64", "media_type": "image/png", "data": b64},
                    }
                )

        if text:
            content.append({"type": "text", "text": f"Document text content:\n\n{text}"})

        if not content:
            return {"document_type": "unknown", "title": "Empty Document", "confidence": 0.0, "content": {}}

        content.append({"type": "text", "text": "Analyze this document and return structured JSON."})

        logger.info("llm_call_start", extra={"provider": "aitrium", "model": self.model})
        start = time.monotonic()
        try:
            response = await self.client.messages.create(
                model=self.model,
                max_tokens=8192,
                system=self._build_system_prompt(extraction_hint),
                messages=[{"role": "user", "content": content}],
            )
        except Exception as e:
            logger.error("llm_call_failed", extra={"provider": "aitrium", "error": str(e)})
            raise
        finally:
            duration_ms = round((time.monotonic() - start) * 1000)
            logger.info("llm_call_end", extra={"provider": "aitrium", "duration_ms": duration_ms})

        raw = response.content[0].text.strip()
        if raw.startswith("```"):
            raw = raw.split("\n", 1)[1].rsplit("```", 1)[0].strip()

        return json.loads(raw)
