---
description: Test document extraction against the running local server
---

Run a test extraction against the local development server.

Ask the user for:
1. File path (required)
2. Document type hint (optional — auto, identity_document, proof_of_address, bank_statement, payslip, invoice, bill)
3. Port — default 8000 (Python) or 5118 (.NET)

Then run:
```bash
curl -X POST http://localhost:{port}/extract \
  -F "file=@{filepath}" \
  -F "document_type={type}" | python -m json.tool
```

From the response, highlight:
- `document_type` and `document_subtype` — what the service thinks it is
- `confidence` — how confident (flag if < 0.7)
- `extraction_metadata.tier` — which tier processed it (azure_di / llm / llm_fallback)
- `extraction_metadata.llm_skipped` — was LLM skipped? (cost saving)
- `extraction_metadata.estimated_cost_savings` — if LLM was skipped
- `extraction_metadata.missing_fields` — any required fields not found
- `validation.issues` — any validation problems (expired, poor quality, etc.)
- `quality.issues` — any image quality problems
