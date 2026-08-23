# Document Intelligence API

A FICA/KYC document intelligence service that extracts structured data from identity documents, proof of address, bank statements, and payslips using LLM vision.

Two implementations: **Python (FastAPI)** and **.NET 9** — both produce identical output.

## Supported Documents

| Type | Subtypes |
|------|----------|
| Identity Documents | National ID, Passport, Driver's License, Temporary ID, Asylum Permit |
| Proof of Address | Utility bill, Bank letter, Lease agreement, Municipal account, Insurance/Government letter, Tax document |
| Bank Statements | Current account, Savings account, Credit card, Loan statement |
| Payslips | Monthly payslip, Annual tax certificate, Employment letter |

## LLM Providers

| Provider | Description | Required Config |
|----------|-------------|-----------------|
| `anthropic` | Direct Anthropic API | `ANTHROPIC_API_KEY` |
| `aitrium` | Gateway proxy (token-based auth) | `AITRIUM_BASE_URL`, `AITRIUM_AUTH_TOKEN`, `AITRIUM_MODEL` |
| `bedrock` | AWS Bedrock (SigV4 signing) | AWS credentials (explicit or default chain) |
| `openai` | OpenAI GPT-4o | `OPENAI_API_KEY` |

---

## Python Service

### Prerequisites

- Python 3.11+
- Tesseract OCR (`brew install tesseract` / `apt install tesseract-ocr`)

### Setup

```bash
python -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
```

### Configuration

Copy `.env.example` to `.env` and fill in your provider credentials:

```bash
cp .env.example .env
```

**Using Anthropic directly:**
```env
DEFAULT_LLM_PROVIDER=anthropic
ANTHROPIC_API_KEY=sk-ant-api03-your-key-here
```

**Using Aitrium gateway:**
```env
DEFAULT_LLM_PROVIDER=aitrium
AITRIUM_BASE_URL=https://your-gateway-url/v1/claude
AITRIUM_AUTH_TOKEN=your-base64-token
AITRIUM_MODEL=your-model-id-or-arn
```

**Using AWS Bedrock:**
```env
DEFAULT_LLM_PROVIDER=bedrock
BEDROCK_REGION=eu-central-1
BEDROCK_MODEL=anthropic.claude-sonnet-4-20250514-v1:0
BEDROCK_ACCESS_KEY=AKIA...
BEDROCK_SECRET_KEY=...
```

**Using OpenAI:**
```env
DEFAULT_LLM_PROVIDER=openai
OPENAI_API_KEY=sk-your-key-here
```

### Run

```bash
uvicorn app.main:app --reload --port 8000
```

Server starts at `http://localhost:8000`. API docs at `http://localhost:8000/docs`.

### Test

```bash
# Health check
curl http://localhost:8000/health

# Extract from a PDF
curl -X POST http://localhost:8000/extract \
  -F "file=@/path/to/document.pdf"

# Extract with hints
curl -X POST http://localhost:8000/extract \
  -F "file=@/path/to/id-card.jpg" \
  -F "document_type=identity_document" \
  -F "hint=South African ID card"

# Override provider per request
curl -X POST http://localhost:8000/extract \
  -F "file=@/path/to/statement.pdf" \
  -F "provider=anthropic"

# Batch (up to 10 files)
curl -X POST http://localhost:8000/extract/batch \
  -F "files=@doc1.pdf" \
  -F "files=@doc2.jpg"
```

### Run Tests

```bash
pip install -r requirements-dev.txt
pytest
```

---

## .NET Service

### Prerequisites

- .NET 9 SDK
- Tesseract OCR (for image extraction)

### Setup

```bash
cd dotnet/DocumentIntelligence.Api
dotnet restore
```

### Configuration

Use `dotnet user-secrets` (recommended) or edit `appsettings.Development.json`.

**Using Anthropic directly:**
```bash
dotnet user-secrets init
dotnet user-secrets set "LlmSettings:DefaultProvider" "anthropic"
dotnet user-secrets set "LlmSettings:AnthropicApiKey" "sk-ant-api03-your-key-here"
```

**Using Aitrium gateway:**
```bash
dotnet user-secrets init
dotnet user-secrets set "LlmSettings:DefaultProvider" "aitrium"
dotnet user-secrets set "LlmSettings:AitriumBaseUrl" "https://your-gateway-url/v1/claude"
dotnet user-secrets set "LlmSettings:AitriumAuthToken" "your-base64-token"
dotnet user-secrets set "LlmSettings:AitriumModel" "your-model-id-or-arn"
```

**Using AWS Bedrock:**
```bash
dotnet user-secrets init
dotnet user-secrets set "LlmSettings:DefaultProvider" "bedrock"
dotnet user-secrets set "LlmSettings:BedrockRegion" "eu-central-1"
dotnet user-secrets set "LlmSettings:BedrockModel" "anthropic.claude-sonnet-4-20250514-v1:0"
dotnet user-secrets set "LlmSettings:BedrockAccessKey" "AKIA..."
dotnet user-secrets set "LlmSettings:BedrockSecretKey" "..."
```

**Using OpenAI:**
```bash
dotnet user-secrets init
dotnet user-secrets set "LlmSettings:DefaultProvider" "openai"
dotnet user-secrets set "LlmSettings:OpenAiApiKey" "sk-your-key-here"
```

### Run

```bash
dotnet run
```

Server starts at `http://localhost:5118` (check console output for exact port).

### Test

```bash
# Health check
curl http://localhost:5118/health

# Extract from a PDF
curl -X POST http://localhost:5118/extract \
  -F "file=@/path/to/document.pdf"

# Extract with hints
curl -X POST http://localhost:5118/extract \
  -F "file=@/path/to/id-card.jpg" \
  -F "document_type=identity_document" \
  -F "hint=South African ID card"

# Override provider per request
curl -X POST http://localhost:5118/extract \
  -F "file=@/path/to/statement.pdf" \
  -F "provider=anthropic"

# Batch (up to 10 files)
curl -X POST http://localhost:5118/extract/batch \
  -F "files=@doc1.pdf" \
  -F "files=@doc2.jpg"
```

---

## API Reference

### `POST /extract`

Extract structured data from a single document.

**Form fields:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `file` | file | required | PDF or image file |
| `document_type` | string | `auto` | Hint: `auto`, `identity_document`, `proof_of_address`, `bank_statement`, `payslip` |
| `provider` | string | config default | Override: `anthropic`, `aitrium`, `bedrock`, `openai` |
| `model` | string | provider default | Model ID override |
| `hint` | string | none | Additional extraction guidance |
| `use_vision` | bool | `true` | Send images to LLM vision |

**Response:**

```json
{
  "document_type": "bank_statement",
  "document_subtype": "current_account",
  "title": "FNB Current Account Statement - Jan 2024",
  "confidence": 0.95,
  "quality": {
    "readable": true,
    "issues": []
  },
  "content": {
    "account_holder": "John Smith",
    "bank_name": "First National Bank",
    "account_number": "62****4321",
    "statement_period": {"from": "2024-01-01", "to": "2024-01-31"},
    "opening_balance": 15420.50,
    "closing_balance": 18230.75,
    "currency": "ZAR",
    "transactions": [
      {"date": "2024-01-05", "description": "Salary Deposit", "type": "credit", "amount": 45000.00, "balance": 60420.50}
    ],
    "total_credits": 48500.00,
    "total_debits": 45689.75
  },
  "validation": {
    "is_expired": null,
    "expiry_date": null,
    "issues": []
  },
  "raw_text": "extracted OCR/PDF text..."
}
```

### `POST /extract/batch`

Extract from up to 10 files in one request. Same form fields as `/extract` but uses `files` (plural) for the file field.

### `GET /health`

Returns `{"status": "healthy"}`.

---

## Supported File Types

PDF, PNG, JPG, JPEG, TIFF, BMP, WebP

Max file size: 50MB (configurable via `MAX_FILE_SIZE_MB`).

## Logging

Both services emit structured logs with correlation IDs for request tracing:

- **Python**: JSON-formatted logs via `python-json-logger`
- **.NET**: Serilog with console sink, `X-Correlation-ID` header propagation

Pass `X-Request-ID` (Python) or `X-Correlation-ID` (.NET) header to trace requests through your infrastructure.
