# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A FICA/KYC document intelligence API that accepts PDF or image uploads and returns structured JSON extraction powered by LLM vision. Two implementations exist side-by-side:
- **Python/FastAPI** — primary service (`app/`)
- **.NET 9 / ASP.NET Core** — parallel port (`dotnet/DocumentIntelligence.Api/`)

Both implementations are kept in parity: same endpoints, same schemas, same logic.

---

## Supported Document Types

| Type | Subtypes |
|------|----------|
| `identity_document` | national_id, passport, drivers_license, temporary_id, asylum_permit |
| `proof_of_address` | utility_bill, bank_letter, lease_agreement, municipal_account, insurance_letter, government_letter, tax_document |
| `bank_statement` | current_account, savings_account, credit_card, loan_statement |
| `payslip` | monthly_payslip, annual_tax_certificate, employment_letter |
| `invoice` | commercial_invoice, proforma_invoice, tax_invoice |
| `bill` | phone_bill, medical_bill, subscription, other_bill |

---

## Architecture

```
app/
├── main.py                          # FastAPI app — all endpoints
├── config.py                        # pydantic-settings, reads .env
├── middleware.py                    # CorrelationId middleware
├── logging_config.py                # JSON structured logging
├── models/schemas.py                # DocumentResponse, StoredDocument
│
├── extractors/
│   ├── pdf_extractor.py             # PyMuPDF: text + page images + page count
│   ├── image_extractor.py           # Tesseract OCR + image prep
│   └── azure_di_extractor.py        # Azure Document Intelligence pre-built models
│
├── llm/
│   ├── base.py                      # LLMProvider ABC + system prompt (all schemas)
│   ├── factory.py                   # Provider registry, auto-fallback to mock
│   ├── anthropic_provider.py        # Direct Anthropic API
│   ├── aitrium_provider.py          # Fiserv gateway proxy (token auth)
│   ├── bedrock_provider.py          # AWS Bedrock (SigV4)
│   ├── openai_provider.py           # OpenAI GPT-4o
│   └── mock_provider.py             # Dev/test — instant fake responses
│
├── services/
│   ├── document_service.py          # LLM extraction path (calls LLM provider)
│   ├── extraction_pipeline.py       # Orchestrates adaptive routing strategy
│   ├── quality_detector.py          # Classifies: digital_pdf | scanned_pdf | photo
│   └── field_requirements.py        # Required fields per doc type, completeness check
│
├── repositories/
│   ├── base.py                      # DocumentRepository ABC
│   ├── document_repository.py       # InMemoryDocumentRepository
│   ├── sqlite_repository.py         # SQLite (aiosqlite, async)
│   ├── cosmos_repository.py         # Azure Cosmos DB stub
│   ├── sql_repository.py            # SQL Server stub
│   └── table_storage_repository.py  # Azure Table Storage stub
│
└── storage/
    ├── base.py                      # FileStorage ABC
    ├── local_storage.py             # Saves to ./uploads/{id}/{filename}
    └── azure_blob_storage.py        # Azure Blob stub
```

---

## Request Flow

```
Upload → ExtractionPipeline
  ├── Select prebuilt model based on document type hint:
  │     ID/passport/license → prebuilt-idDocument
  │     Invoice             → prebuilt-invoice
  │     Bill/receipt        → prebuilt-receipt
  │     Other/unknown       → prebuilt-read
  ├── Azure DI first (ALL document types — photos, scans, digital PDFs)
  │     → confidence ≥ 0.85 + all fields → Accept (no LLM cost)
  │     → confidence 0.65–0.85 + all fields → Accept with warning
  │     → confidence < 0.65 OR missing fields → LLM Vision fallback
  ├── File saved to storage (./uploads/{id}/{filename})
  ├── Result saved to repository
  └── StoredDocument returned (full audit trail)
```

---

## Extraction Strategies

Controlled by `EXTRACTION_STRATEGY` env var / `Extraction:Strategy` appsetting.

| Strategy | Behaviour |
|----------|-----------|
| `adaptive` | **Default.** ALL docs → Azure DI first (smart prebuilt model selection) → LLM only if confidence low or fields missing |
| `llm_only` | Always use LLM. Highest quality, highest cost |
| `azure_di_first` | Try Azure DI, fall back to LLM if confidence < threshold |
| `ocr_first` | Try OCR (no vision), fall back to LLM if confidence < threshold |
| `hybrid` | Run Azure DI + LLM in parallel, merge and flag discrepancies |

Adaptive routing decision tree:
```
ALL documents (photo, scan, digital PDF):
  → Select prebuilt model (idDocument / invoice / receipt / read)
  → Azure DI unavailable/error → LLM fallback
  → Azure DI confidence < 0.65 → LLM fallback
  → Azure DI confidence ≥ 0.85 + all required fields present → Accept (saves ~95%)
  → Azure DI confidence 0.65-0.85 + all fields → Accept with warning
  → Missing critical fields → LLM fallback
```

---

## Build, Test, and Run Commands

### Python

```bash
# One-time setup
python -m venv .venv && source .venv/bin/activate
pip install -r requirements-dev.txt

# Run dev server (auto-uses mock if no API keys configured)
uvicorn app.main:app --reload --port 8000

# Run tests
pytest
pytest tests/test_api.py -v
pytest tests/test_providers.py -v
pytest tests/test_repository.py -v

# Lint / format
ruff check .
ruff format .
```

### .NET

```bash
cd dotnet/DocumentIntelligence.Api

# Build
dotnet build

# Run (auto-uses mock in Development when no credentials set)
dotnet run

# Tests
dotnet test ../../dotnet/DocumentIntelligence.Tests/

# Set secrets for a real provider
dotnet user-secrets init
dotnet user-secrets set "LlmSettings:AnthropicApiKey" "sk-ant-..."
dotnet user-secrets set "LlmSettings:DefaultProvider" "anthropic"
```

---

## Configuration Reference

### Python (`.env`)

```env
# LLM Provider
DEFAULT_LLM_PROVIDER=anthropic          # anthropic | aitrium | bedrock | openai | mock
ANTHROPIC_API_KEY=sk-ant-...
OPENAI_API_KEY=sk-...

# Aitrium gateway
AITRIUM_BASE_URL=https://your-gateway/v1/claude
AITRIUM_AUTH_TOKEN=your-base64-token
AITRIUM_MODEL=your-model-id-or-arn

# AWS Bedrock
BEDROCK_REGION=eu-central-1
BEDROCK_MODEL=anthropic.claude-sonnet-4-20250514-v1:0
BEDROCK_ACCESS_KEY=
BEDROCK_SECRET_KEY=
BEDROCK_SESSION_TOKEN=

# Extraction
EXTRACTION_STRATEGY=adaptive            # adaptive | llm_only | ocr_first | azure_di_first | hybrid
CONFIDENCE_THRESHOLD=0.85
AZURE_DI_ENDPOINT=
AZURE_DI_KEY=

# Persistence
PERSISTENCE_BACKEND=sqlite              # memory | sqlite | cosmos | sql | table_storage
DATABASE_URL=documents.db

# Storage
STORAGE_BACKEND=local                   # local | azure_blob
STORAGE_PATH=./uploads

# Upload limits
MAX_FILE_SIZE_MB=50
ALLOWED_EXTENSIONS=pdf,png,jpg,jpeg,tiff,bmp,webp
```

### .NET (`appsettings.json` / user-secrets)

```json
{
  "LlmSettings": {
    "DefaultProvider": "anthropic",
    "AnthropicApiKey": "",
    "OpenAiApiKey": "",
    "AitriumBaseUrl": "",
    "AitriumAuthToken": "",
    "AitriumModel": "",
    "BedrockRegion": "eu-central-1",
    "BedrockModel": "anthropic.claude-sonnet-4-20250514-v1:0",
    "BedrockAccessKey": "",
    "BedrockSecretKey": "",
    "BedrockSessionToken": ""
  },
  "Extraction": {
    "Strategy": "adaptive",
    "ConfidenceThreshold": 0.85,
    "AzureDiEndpoint": "",
    "AzureDiKey": ""
  },
  "Persistence": { "Backend": "sqlite" },
  "Storage": { "Backend": "local", "Path": "./uploads" },
  "ConnectionStrings": { "Documents": "Data Source=documents.db" }
}
```

---

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Health check |
| `POST` | `/extract` | Auto-detect document type |
| `POST` | `/extract/identity` | Identity documents |
| `POST` | `/extract/bank-statement` | Bank statements |
| `POST` | `/extract/proof-of-address` | Proof of address |
| `POST` | `/extract/payslip` | Payslips |
| `POST` | `/extract/invoice` | Invoices |
| `POST` | `/extract/bill` | Bills |
| `POST` | `/extract/batch` | Batch (up to 10 files) |
| `GET` | `/documents` | List stored results (`?type=&limit=&offset=`) |
| `GET` | `/documents/{id}` | Get by ID |
| `GET` | `/documents/{id}/file` | Download original file |
| `DELETE` | `/documents/{id}` | Delete record + file |

All `/extract*` endpoints accept `multipart/form-data`:
- `file` (required) — PDF or image
- `document_type` — optional hint: `auto`, `identity_document`, `proof_of_address`, `bank_statement`, `payslip`, `invoice`, `bill`
- `provider` — override: `anthropic`, `aitrium`, `bedrock`, `openai`, `mock`
- `model` — model ID override
- `hint` — additional extraction guidance text
- `use_vision` — bool, default `true`

---

## LLM Providers

| Provider | Auth | When to use |
|----------|------|-------------|
| `anthropic` | `ANTHROPIC_API_KEY` | Direct Anthropic API |
| `aitrium` | `AITRIUM_AUTH_TOKEN` + `AITRIUM_BASE_URL` | Fiserv gateway proxy |
| `bedrock` | AWS credentials or default chain | Direct AWS Bedrock (SigV4) |
| `openai` | `OPENAI_API_KEY` | GPT-4o |
| `mock` | none | Development, no API calls |

Auto-fallback to `mock` when no credentials are configured (Python: all keys empty; .NET: Development environment).

---

## Persistence Backends

| Backend | Config value | Notes |
|---------|-------------|-------|
| In-memory | `memory` | Dev default. Lost on restart |
| SQLite | `sqlite` | Prod default. `documents.db` file |
| Cosmos DB | `cosmos` | Stub — implement `CosmosDocumentRepository` |
| SQL Server | `sql` | Stub — implement `SqlDocumentRepository` |
| Azure Table | `table_storage` | Stub — implement `TableStorageDocumentRepository` |

---

## Key Design Decisions

- **Dual implementation parity** — Python and .NET stay in sync. Same endpoints, same schemas, same system prompt.
- **Interface-first persistence** — `IDocumentRepository` / `DocumentRepository` ABC means swapping backends requires only a config change.
- **Interface-first storage** — `IFileStorage` / `FileStorage` ABC; local disk now, Azure Blob later.
- **Interface-first LLM** — `ILlmProvider` / `LLMProvider` ABC; adding a new provider means one file + factory registration.
- **Mock for dev** — `EXTRACTION_STRATEGY=adaptive` + no credentials = instant mock responses, no network needed.
- **Adaptive routing by default** — never pay for LLM when Azure DI is sufficient. Photos/scans bypass Azure DI entirely.
- **Full audit trail** — every extraction stores: original file, timestamps, provider/model used, processing duration, page count.
- **Correlation IDs** — every request gets a UUID logged throughout the pipeline (`X-Request-ID` / `X-Correlation-ID`).
- **No Fiserv values in source** — all internal URLs, ARNs, and tokens must go in user-secrets or `.env`, never committed.
