---
description: Run all tests for both Python and .NET
---

Run the full test suite for the document intelligence API.

**Python tests:**
```bash
source .venv/bin/activate
pytest tests/ -v
```

**Python test coverage by file:**
- `tests/test_api.py` — endpoint tests (extract, batch, health, documents CRUD)
- `tests/test_providers.py` — provider factory and mock provider
- `tests/test_repository.py` — in-memory and SQLite repository

**.NET tests:**
```bash
dotnet test dotnet/DocumentIntelligence.Tests/
```

**.NET test coverage:**
- `MockProviderTests.cs` — mock provider responses by filename hint
- `InMemoryRepositoryTests.cs` — CRUD, filter, pagination
- `LlmProviderFactoryTests.cs` — provider selection and auto-fallback

Report a summary: total tests, passed, failed, any failures with stack traces.
