from app.llm.anthropic_provider import AnthropicProvider
from app.llm.base import LLMProvider
from app.llm.openai_provider import OpenAIProvider

PROVIDERS: dict[str, type[LLMProvider]] = {
    "anthropic": AnthropicProvider,
    "openai": OpenAIProvider,
}


def get_llm_provider(provider_name: str | None = None, **kwargs) -> LLMProvider:
    from app.config import settings

    name = (provider_name or settings.default_llm_provider).lower()
    if name not in PROVIDERS:
        raise ValueError(f"Unknown LLM provider: {name}. Available: {list(PROVIDERS.keys())}")
    return PROVIDERS[name](**kwargs)
