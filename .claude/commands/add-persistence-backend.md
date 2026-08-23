---
description: Implement a persistence backend (Cosmos DB, SQL Server, or Azure Table Storage)
---

Implement one of the stub persistence backends.

Ask the user which backend to implement:
1. `cosmos` — Azure Cosmos DB
2. `sql` — SQL Server / PostgreSQL
3. `table_storage` — Azure Table Storage

**Python — implement the stub in:**
- `app/repositories/cosmos_repository.py` (or sql / table_storage)
- Must implement all methods from `DocumentRepository` ABC in `app/repositories/base.py`:
  - `save(document, filename)`
  - `get(doc_id)`
  - `list_all(limit, offset, document_type)`
  - `delete(doc_id)`
- Add required packages to `requirements.txt`
- Add config fields to `app/config.py` (connection strings, keys)
- Add env vars to `.env.example`

**.NET — implement the stub in:**
- `dotnet/.../Repositories/CosmosDocumentRepository.cs` (or Sql / TableStorage)
- Must implement all methods from `IDocumentRepository`:
  - `Save(document)`
  - `Get(id)`
  - `ListAll(limit, offset, documentType)`
  - `Delete(id)`
- Add NuGet packages to `.csproj`
- Add config to `appsettings.json` (connection strings, endpoints)
- Register in `Program.cs` backend switch

For Cosmos: use the `Fiserv.Azure.Cosmos` patterns if inside the Fiserv platform, otherwise use the standard `Microsoft.Azure.Cosmos` SDK.
