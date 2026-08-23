# Usage Guide

Practical guide for integrating with the Document Intelligence API.

---

## 1. Getting Started

### Run in development (no API keys needed)

**Python:**
```bash
python -m venv .venv && source .venv/bin/activate
pip install -r requirements-dev.txt
uvicorn app.main:app --reload --port 8000
```

**.NET:**
```bash
cd dotnet/DocumentIntelligence.Api
dotnet run
# Starts at http://localhost:5118 by default
```

Both auto-use the **mock provider** when no API keys are configured — no `.env` or `user-secrets` needed to get started.

### Verify it's running

```bash
curl http://localhost:8000/health
# {"status": "healthy"}
```

---

## 2. Basic Extraction

Upload any supported document and get structured JSON back:

```bash
curl -X POST http://localhost:8000/extract \
  -F "file=@invoice.pdf" | python -m json.tool
```

### Response fields explained

```json
{
  "id": "a1b2c3d4-...",               // UUID — use this to retrieve later
  "filename": "invoice.pdf",           // original filename
  "file_size_bytes": 245120,           // bytes
  "file_content_type": "application/pdf",
  "storage_path": "uploads/a1b2.../invoice.pdf",  // where original is stored
  "uploaded_at": "2026-08-23T14:30:00Z",
  "processed_at": "2026-08-23T14:30:01Z",
  "processing_duration_ms": 1240,      // how long extraction took
  "provider_used": "AitriumProvider",  // which LLM provider ran
  "model_used": "arn:aws:bedrock:...", // which model was used
  "page_count": 2,                     // null for images
  "document_type": "invoice",          // detected type
  "document_subtype": "tax_invoice",   // more specific subtype
  "title": "Invoice INV-2024-001",
  "confidence": 0.94,                  // 0.0–1.0 overall confidence
  "quality": {
    "readable": true,
    "issues": []                       // blurry, cropped, skewed, etc.
  },
  "content": {                         // structured fields — varies by doc type
    "vendor_name": "Acme Corp",
    "invoice_number": "INV-2024-001",
    "total_amount": 11400.00,
    "currency": "ZAR",
    "line_items": [...]
  },
  "validation": {
    "is_expired": null,
    "expiry_date": null,
    "issues": []                       // expired, older_than_3_months, etc.
  },
  "extraction_metadata": {             // routing decision info
    "strategy_used": "adaptive",
    "tier": "azure_di",
    "llm_skipped": true,
    "estimated_cost_savings": "~95% vs LLM vision"
  }
}
```

---

## 3. Using Type Hints

Type hints tell the LLM what kind of document to expect — they improve accuracy and speed.

### Option A: Typed endpoint (pre-set hint)

```bash
# Identity documents
curl -X POST http://localhost:8000/extract/identity \
  -F "file=@passport.pdf"

# Bank statements
curl -X POST http://localhost:8000/extract/bank-statement \
  -F "file=@statement.pdf"

# Payslips
curl -X POST http://localhost:8000/extract/payslip \
  -F "file=@payslip.pdf"

# Invoices
curl -X POST http://localhost:8000/extract/invoice \
  -F "file=@invoice.pdf"

# Bills (phone, medical, subscriptions)
curl -X POST http://localhost:8000/extract/bill \
  -F "file=@phone-bill.pdf"

# Proof of address
curl -X POST http://localhost:8000/extract/proof-of-address \
  -F "file=@utility-bill.pdf"
```

### Option B: Generic endpoint with document_type field

```bash
curl -X POST http://localhost:8000/extract \
  -F "file=@invoice.pdf" \
  -F "document_type=invoice"
```

### Both options are identical under the hood

The typed endpoints just pre-fill `document_type` for you. Use whichever fits your integration better.

### Add extra context with `hint`

```bash
curl -X POST http://localhost:8000/extract/identity \
  -F "file=@id.jpg" \
  -F "hint=South African green ID book, issued before 2013"
```

---

## 4. Batch Processing

Upload up to 10 files in a single request:

```bash
curl -X POST http://localhost:8000/extract/batch \
  -F "files=@doc1.pdf" \
  -F "files=@doc2.jpg" \
  -F "files=@doc3.png"
```

Response is an array of `StoredDocument` objects — one per successfully processed file. Files that fail are silently skipped (check array length vs files sent).

```bash
# With type hint and provider override for all files
curl -X POST http://localhost:8000/extract/batch \
  -F "files=@invoice1.pdf" \
  -F "files=@invoice2.pdf" \
  -F "document_type=invoice" \
  -F "provider=anthropic"
```

---

## 5. Retrieving Past Results

Every extraction is stored and retrievable by ID.

### List all documents

```bash
curl "http://localhost:8000/documents"
```

### Filter by type

```bash
curl "http://localhost:8000/documents?type=invoice"
curl "http://localhost:8000/documents?type=bank_statement"
curl "http://localhost:8000/documents?type=identity_document"
```

### Pagination

```bash
# First 20
curl "http://localhost:8000/documents?limit=20&offset=0"

# Next 20
curl "http://localhost:8000/documents?limit=20&offset=20"
```

### Get a specific document

```bash
curl "http://localhost:8000/documents/a1b2c3d4-1234-5678-abcd-ef0123456789"
```

### Delete a document

```bash
curl -X DELETE "http://localhost:8000/documents/a1b2c3d4-..."
# Returns 204 No Content on success
```

---

## 6. Downloading Original Files

Retrieve the original uploaded file exactly as it was submitted:

```bash
# Download to file
curl "http://localhost:8000/documents/a1b2c3d4-.../file" \
  -o original_document.pdf

# Stream and inspect
curl "http://localhost:8000/documents/a1b2c3d4-.../file" \
  --head  # Shows Content-Type and Content-Disposition headers
```

The file is served with the original `Content-Type` and filename.

---

## 7. Choosing a Provider

Override the default LLM provider per request:

```bash
# Use Anthropic directly
curl -X POST http://localhost:8000/extract \
  -F "file=@doc.pdf" \
  -F "provider=anthropic"

# Use Aitrium gateway
curl -X POST http://localhost:8000/extract \
  -F "file=@doc.pdf" \
  -F "provider=aitrium"

# Use AWS Bedrock
curl -X POST http://localhost:8000/extract \
  -F "file=@doc.pdf" \
  -F "provider=bedrock"

# Use OpenAI GPT-4o
curl -X POST http://localhost:8000/extract \
  -F "file=@doc.pdf" \
  -F "provider=openai"

# Force mock provider (instant fake response)
curl -X POST http://localhost:8000/extract \
  -F "file=@doc.pdf" \
  -F "provider=mock"

# Override model for a specific call
curl -X POST http://localhost:8000/extract \
  -F "file=@doc.pdf" \
  -F "provider=anthropic" \
  -F "model=claude-opus-4-5"
```

---

## 8. Understanding Extraction Metadata

Every response includes `extraction_metadata` explaining the routing decision:

```json
"extraction_metadata": {
  "strategy_used": "adaptive",
  "quality_tier": "digital_pdf",
  "tier": "azure_di",
  "llm_skipped": true,
  "tier1_confidence": 0.94,
  "field_completeness": true,
  "missing_fields": [],
  "estimated_cost_savings": "~95% vs LLM vision"
}
```

| Field | Meaning |
|-------|---------|
| `strategy_used` | Which strategy was applied (`adaptive`, `llm_only`, etc.) |
| `quality_tier` | How the document was classified: `digital_pdf`, `scanned_pdf`, or `photo` |
| `tier` | What actually ran: `azure_di`, `llm_direct`, `llm_fallback`, `hybrid` |
| `llm_skipped` | `true` = LLM was not called (cost saved) |
| `tier1_confidence` | Azure DI confidence score (if it ran) |
| `field_completeness` | Whether all required fields were found |
| `missing_fields` | Which critical fields were absent (caused LLM fallback if any) |
| `reason` | Why the routing decision was made (e.g., `"missing_fields: [transactions]"`) |
| `estimated_cost_savings` | Human-readable cost saving (shown when LLM skipped) |

### Example: LLM fallback due to low confidence

```json
"extraction_metadata": {
  "strategy_used": "adaptive",
  "quality_tier": "scanned_pdf",
  "tier": "llm_direct",
  "llm_skipped": false,
  "reason": "document_is_image_based"
}
```

### Example: LLM fallback due to missing fields

```json
"extraction_metadata": {
  "strategy_used": "adaptive",
  "quality_tier": "digital_pdf",
  "tier": "llm_fallback",
  "llm_skipped": false,
  "tier1_confidence": 0.72,
  "reason": "missing_fields: ['transactions']"
}
```

---

## 9. Extraction Strategies

Set via `DEFAULT_LLM_PROVIDER` or pass `provider=` per request. Strategy is set via `EXTRACTION_STRATEGY` env var.

| Strategy | When to use |
|----------|-------------|
| `adaptive` | **Default. Best for most cases.** Routes intelligently based on quality and confidence |
| `llm_only` | When you need maximum quality and accuracy, cost not a concern |
| `azure_di_first` | When most of your docs are clean digital PDFs (invoices, ID scans) |
| `ocr_first` | When docs are simple and text-heavy; very cheap but less accurate |
| `hybrid` | When you need cross-validation between two sources for compliance |

### Set strategy globally

**Python `.env`:**
```env
EXTRACTION_STRATEGY=adaptive
CONFIDENCE_THRESHOLD=0.85
```

**.NET `appsettings.json`:**
```json
"Extraction": {
  "Strategy": "adaptive",
  "ConfidenceThreshold": 0.85
}
```

---

## 10. Cost Optimization Tips

1. **Use `adaptive` strategy (default)** — automatically skips LLM for clean digital PDFs when Azure DI is confident. Expect ~70–95% cost reduction on well-formatted documents.

2. **Configure Azure Document Intelligence** — without it, `adaptive` falls through to LLM for everything. Set `AZURE_DI_ENDPOINT` and `AZURE_DI_KEY` to unlock real savings.

3. **Use typed endpoints for known document types** — `/extract/invoice` gives the LLM a clearer hint, reducing tokens needed for type detection.

4. **Use `use_vision=false` for clean digital PDFs** — if you know a PDF has extractable text and you're willing to skip image analysis:
   ```bash
   curl -X POST http://localhost:8000/extract \
     -F "file=@statement.pdf" \
     -F "use_vision=false"
   ```

5. **Monitor `llm_skipped` in responses** — if it's always `false`, Azure DI isn't saving you money. Check your endpoint and key config.

6. **Use batch for multiple files** — one HTTP connection for up to 10 files.

7. **Tune `CONFIDENCE_THRESHOLD`** — lower it (e.g., `0.75`) to trust Azure DI more and skip LLM more often. Raise it (e.g., `0.90`) for stricter LLM fallback.

---

## 11. Error Handling

| Status | Meaning | Example |
|--------|---------|---------|
| `400` | Bad request — invalid file type, file too large, missing file, unknown provider | `{"error": "File type '.exe' not supported"}` |
| `404` | Document not found | `{"error": "Document abc-123 not found"}` |
| `422` | Extraction failed — LLM error, unparseable response | `{"error": "Document processing failed", "detail": "..."}` |
| `200` | Success | Full `StoredDocument` JSON |

### Common errors

**File type not supported:**
```json
{"error": "File type '.docx' not supported", "detail": "Allowed: pdf, png, jpg, jpeg, tiff, bmp, webp"}
```
→ Convert to PDF or export as image first.

**File too large:**
```json
{"error": "File exceeds maximum size of 50MB"}
```
→ Compress the PDF or split into pages.

**Unknown provider:**
```json
{"error": "Unknown LLM provider: gpt5. Available: anthropic, aitrium, bedrock, openai, mock"}
```

**Extraction failed:**
```json
{"error": "Document processing failed", "detail": "Anthropic API key not configured"}
```
→ Set your API key in `.env` or user-secrets.

---

## 12. Integration Examples

### Python (requests)

```python
import requests

# Basic extraction
with open("invoice.pdf", "rb") as f:
    response = requests.post(
        "http://localhost:8000/extract/invoice",
        files={"file": ("invoice.pdf", f, "application/pdf")},
        data={"provider": "anthropic", "hint": "Supplier invoice from Cape Town"}
    )

doc = response.json()
print(f"Type: {doc['document_type']}")
print(f"Confidence: {doc['confidence']}")
print(f"LLM skipped: {doc['extraction_metadata']['llm_skipped']}")
print(f"Total amount: {doc['content'].get('total_amount')}")

# Retrieve later
doc_id = doc["id"]
result = requests.get(f"http://localhost:8000/documents/{doc_id}")

# Download original
file_response = requests.get(f"http://localhost:8000/documents/{doc_id}/file")
with open("original.pdf", "wb") as f:
    f.write(file_response.content)

# List invoices
invoices = requests.get(
    "http://localhost:8000/documents",
    params={"type": "invoice", "limit": 20}
).json()
```

### JavaScript (fetch)

```javascript
// Basic extraction
async function extractDocument(file) {
  const form = new FormData();
  form.append("file", file);
  form.append("provider", "anthropic");

  const response = await fetch("http://localhost:8000/extract", {
    method: "POST",
    body: form,
  });

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.error);
  }

  return response.json();
}

// List and filter
async function listDocuments(type, limit = 20, offset = 0) {
  const params = new URLSearchParams({ limit, offset });
  if (type) params.set("type", type);

  const response = await fetch(`http://localhost:8000/documents?${params}`);
  return response.json();
}

// Download original file
async function downloadFile(docId) {
  const response = await fetch(`http://localhost:8000/documents/${docId}/file`);
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = response.headers.get("Content-Disposition")?.split("filename=")[1] ?? "document";
  a.click();
}
```

### C# (HttpClient)

```csharp
using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5118") };

// Basic extraction
using var form = new MultipartFormDataContent();
var fileBytes = await File.ReadAllBytesAsync("invoice.pdf");
form.Add(new ByteArrayContent(fileBytes), "file", "invoice.pdf");
form.Add(new StringContent("anthropic"), "provider");

var response = await http.PostAsync("/extract/invoice", form);
response.EnsureSuccessStatusCode();

var json = await response.Content.ReadAsStringAsync();
var doc = JsonSerializer.Deserialize<JsonDocument>(json);
var docId = doc!.RootElement.GetProperty("id").GetString();

Console.WriteLine($"Document type: {doc.RootElement.GetProperty("document_type").GetString()}");
Console.WriteLine($"LLM skipped: {doc.RootElement.GetProperty("extraction_metadata").GetProperty("llm_skipped").GetBoolean()}");

// Retrieve by ID
var getResponse = await http.GetAsync($"/documents/{docId}");
var stored = await getResponse.Content.ReadAsStringAsync();

// List with filter
var list = await http.GetAsync("/documents?type=invoice&limit=20");

// Download original file
var fileResponse = await http.GetAsync($"/documents/{docId}/file");
var originalBytes = await fileResponse.Content.ReadAsByteArrayAsync();
await File.WriteAllBytesAsync("original.pdf", originalBytes);
```

---

## Port Reference

| Service | Default Port |
|---------|-------------|
| Python (uvicorn) | `8000` |
| .NET (Development) | `5118` (check console output) |

Both expose the same endpoints. Swagger UI available at `/docs` (Python) and `/openapi` (both in Development mode).
