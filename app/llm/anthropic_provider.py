import base64
import json
from typing import Any

import anthropic

from app.config import settings
from app.llm.base import LLMProvider


class AnthropicProvider(LLMProvider):
    def __init__(self, api_key: str | None = None, model: str = "claude-sonnet-4-20250514"):
        self.client = anthropic.AsyncAnthropic(api_key=api_key or settings.anthropic_api_key)
        self.model = model

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
                        "source": {
                            "type": "base64",
                            "media_type": "image/png",
                            "data": b64,
                        },
                    }
                )

        if text:
            content.append({"type": "text", "text": f"Document text content:\n\n{text}"})

        if not content:
            return {
                "document_type": "unknown",
                "title": "Empty Document",
                "confidence": 0.0,
                "content": {},
            }

        content.append(
            {"type": "text", "text": "Analyze this document and return structured JSON."}
        )

        response = await self.client.messages.create(
            model=self.model,
            max_tokens=8192,
            system=self._build_system_prompt(extraction_hint),
            messages=[{"role": "user", "content": content}],
        )

        raw = response.content[0].text.strip()
        if raw.startswith("```"):
            raw = raw.split("\n", 1)[1].rsplit("```", 1)[0].strip()

        return json.loads(raw)
