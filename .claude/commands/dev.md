---
description: Start the development server (Python or .NET) with mock provider
---

Start the local development server. No API keys or credentials needed — the service auto-falls back to the mock provider.

**Python:**
```bash
source .venv/bin/activate
uvicorn app.main:app --reload --port 8000
```
Runs at: http://localhost:8000
Swagger UI: http://localhost:8000/docs

**NET:**
```bash
cd dotnet/DocumentIntelligence.Api && dotnet run
```
Runs at: http://localhost:5118 (check console for exact port)

**Available endpoints:**
- `GET  /health`
- `POST /extract`                — auto-detect
- `POST /extract/identity`       — identity docs
- `POST /extract/bank-statement` — bank statements
- `POST /extract/proof-of-address`
- `POST /extract/payslip`
- `POST /extract/invoice`
- `POST /extract/bill`
- `POST /extract/batch`          — up to 10 files
- `GET  /documents`              — list stored results
- `GET  /documents/{id}`
- `GET  /documents/{id}/file`    — download original
- `DELETE /documents/{id}`

Show the user which port the server started on and confirm it's healthy with a curl to `/health`.
