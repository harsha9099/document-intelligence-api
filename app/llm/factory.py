from app.llm.aitrium_provider import AitriumProvider
from app.llm.anthropic_provider import AnthropicProvider
from app.llm.base import LLMProvider
from app.llm.bedrock_provider import BedrockProvider
from app.llm.mock_provider import MockProvider
from app.llm.openai_provider import OpenAIProvider

PROVIDERS: dict[str, type[LLMProvider]] = {
    "anthropic": AnthropicProvider,
    "aitrium": AitriumProvider,
    "bedrock": BedrockProvider,
    "openai": OpenAIProvider,
    "mock": MockProvider,
}


def _no_keys_configured() -> bool:
    from app.config import settings
    return (
        not settings.anthropic_api_key
        and not settings.openai_api_key
        and not settings.aitrium_auth_token
        and not settings.bedrock_access_key
    )


def get_llm_provider(provider_name: str | None = None, **kwargs) -> LLMProvider:
    from app.config import settings

    name = (provider_name or settings.default_llm_provider).lower()

    # Auto-fallback to mock when no credentials are configured
    if name != "mock" and name not in ("bedrock",) and _no_keys_configured():
        name = "mock"

    if name not in PROVIDERS:
        raise ValueError(f"Unknown LLM provider: {name}. Available: {list(PROVIDERS.keys())}")
    return PROVIDERS[name](**kwargs)
