---
description: Add a new document type to the extraction schema
---

Add a new document type end-to-end across both implementations.

Ask the user for:
1. Type name (e.g., `tax_return`)
2. `document_type` value (snake_case, e.g., `tax_return`)
3. Subtypes (e.g., `individual_tax_return`, `corporate_tax_return`)
4. Required content fields (the fields that MUST be present for a complete extraction)
5. All content fields (full schema)

Then update these files:

**System prompt (both):**
- `app/llm/base.py` — add new `## {TYPE}` section with `document_subtype` and `"content"` schema
- `dotnet/.../LlmProviders/LlmSystemPrompt.cs` — same section

**Field requirements (both):**
- `app/services/field_requirements.py` — add entry to `REQUIRED_FIELDS` dict
- `dotnet/.../Services/FieldRequirements.cs` — add entry to `RequiredFields` dictionary

**Mock provider (both):**
- `app/llm/mock_provider.py` — add sample data for the new type, add keyword detection in `_detect_type`
- `dotnet/.../LlmProviders/MockProvider.cs` — same

**Endpoints (both):**
- `app/main.py` — add value to `DocumentTypeHint` enum, add `POST /extract/{type-slug}` endpoint
- `dotnet/.../Program.cs` — add `app.MapPost("/extract/{type-slug}", ...)` endpoint

**Update document_type in system prompt header** — add new type to the `document_type` enum string in both system prompts.
