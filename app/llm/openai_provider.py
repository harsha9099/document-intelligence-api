import base64
import json
from typing import Any

import openai

from app.config import settings
from app.llm.base import LLMProvider


class OpenAIProvider(LLMProvider):
    def __init__(self, api_key: str | None = None, model: str = "gpt-4o"):
        self.client = openai.AsyncOpenAI(api_key=api_key or settings.openai_api_key)
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
                        "type": "image_url",
                        "image_url": {"url": f"data:image/png;base64,{b64}", "detail": "high"},
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

        response = await self.client.chat.completions.create(
            model=self.model,
            max_tokens=8192,
            messages=[
                {"role": "system", "content": self._build_system_prompt(extraction_hint)},
                {"role": "user", "content": content},
            ],
        )

        raw = response.choices[0].message.content.strip()
        if raw.startswith("```"):
            raw = raw.split("\n", 1)[1].rsplit("```", 1)[0].strip()

        return json.loads(raw)
