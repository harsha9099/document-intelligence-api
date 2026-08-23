---
description: Scaffold a new LLM provider for both Python and .NET
---

Add a new LLM provider to both the Python and .NET implementations.

Ask the user for:
1. Provider name (e.g., `gemini`)
2. Package/SDK to use (e.g., `google-generativeai`)
3. Auth method (API key / OAuth / token)
4. Base URL if applicable
5. Default model ID

Then create:

**Python:**
- `app/llm/{name}_provider.py` — class `{Name}Provider(LLMProvider)` implementing `analyze_document(text, images, extraction_hint)`
- Add to `PROVIDERS` dict in `app/llm/factory.py`
- Add config fields to `app/config.py` (e.g., `gemini_api_key: str = ""`)
- Add package to `requirements.txt`
- Add env vars to `.env.example`

**.NET:**
- `dotnet/DocumentIntelligence.Api/LlmProviders/{Name}Provider.cs` — implements `ILlmProvider` with `Name`, `ModelUsed`, `AnalyzeDocumentAsync`
- Add to factory switch in `LlmProviderFactory.cs`
- Add config keys to `appsettings.json` and `appsettings.Development.json` (empty values)
- Add NuGet package to `.csproj` if needed

Patterns to follow: look at `AnthropicProvider` for a clean reference implementation.
