import json
import logging
import time
from typing import Any

import boto3

from app.config import settings
from app.llm.base import LLMProvider

logger = logging.getLogger(__name__)


class BedrockProvider(LLMProvider):
    def __init__(
        self,
        model: str = "anthropic.claude-sonnet-4-20250514-v1:0",
        region: str | None = None,
        access_key: str | None = None,
        secret_key: str | None = None,
        session_token: str | None = None,
        endpoint_url: str | None = None,
    ):
        self.model = model or settings.bedrock_model
        self._region = region or settings.bedrock_region or "eu-central-1"
        self._access_key = access_key or settings.bedrock_access_key
        self._secret_key = secret_key or settings.bedrock_secret_key
        self._session_token = session_token or settings.bedrock_session_token
        self._endpoint_url = endpoint_url or settings.bedrock_endpoint

    def _create_client(self):
        kwargs: dict[str, Any] = {
            "service_name": "bedrock-runtime",
            "region_name": self._region,
        }

        if self._access_key and self._secret_key:
            kwargs["aws_access_key_id"] = self._access_key
            kwargs["aws_secret_access_key"] = self._secret_key
            if self._session_token:
                kwargs["aws_session_token"] = self._session_token

        if self._endpoint_url:
            kwargs["endpoint_url"] = self._endpoint_url

        return boto3.client(**kwargs)

    async def analyze_document(
        self,
        text: str | None = None,
        images: list[bytes] | None = None,
        extraction_hint: str | None = None,
    ) -> dict[str, Any]:
        content = []

        if images:
            for img in images[:20]:
                content.append({"image": {"format": "png", "source": {"bytes": img}}})

        if text:
            content.append({"text": f"Document text content:\n\n{text}"})

        if not content:
            return {"document_type": "unknown", "title": "Empty Document", "confidence": 0.0, "content": {}}

        content.append({"text": "Analyze this document and return structured JSON."})

        client = self._create_client()

        logger.info("llm_call_start", extra={"provider": "bedrock", "model": self.model})
        start = time.monotonic()
        try:
            response = client.converse(
                modelId=self.model,
                messages=[{"role": "user", "content": content}],
                system=[{"text": self._build_system_prompt(extraction_hint)}],
                inferenceConfig={"maxTokens": 8192},
            )
        except Exception as e:
            logger.error("llm_call_failed", extra={"provider": "bedrock", "error": str(e)})
            raise
        finally:
            duration_ms = round((time.monotonic() - start) * 1000)
            logger.info("llm_call_end", extra={"provider": "bedrock", "duration_ms": duration_ms})

        raw = response["output"]["message"]["content"][0]["text"].strip()
        if raw.startswith("```"):
            raw = raw.split("\n", 1)[1].rsplit("```", 1)[0].strip()

        return json.loads(raw)
