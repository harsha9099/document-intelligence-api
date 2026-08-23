using System.Text.Json;
using DocumentIntelligence.Api.Models;
using Microsoft.Data.Sqlite;

namespace DocumentIntelligence.Api.Repositories;

public class SqliteDocumentRepository : IDocumentRepository
{
    private readonly string _connectionString;

    private static readonly (string Name, string Def)[] NewColumns =
    [
        ("file_size_bytes", "INTEGER DEFAULT 0"),
        ("file_content_type", "TEXT DEFAULT ''"),
        ("storage_path", "TEXT"),
        ("uploaded_at", "TEXT DEFAULT ''"),
        ("processed_at", "TEXT DEFAULT ''"),
        ("processing_duration_ms", "INTEGER DEFAULT 0"),
        ("provider_used", "TEXT DEFAULT ''"),
        ("model_used", "TEXT"),
        ("page_count", "INTEGER"),
        ("extraction_metadata", "TEXT DEFAULT '{}'"),
    ];

    public SqliteDocumentRepository(string connectionString)
    {
        _connectionString = connectionString;
        EnsureCreated();
    }

    private void EnsureCreated()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                filename TEXT,
                document_type TEXT,
                document_subtype TEXT,
                title TEXT,
                confidence REAL,
                content TEXT,
                quality TEXT,
                validation TEXT,
                raw_text TEXT,
                created_at TEXT DEFAULT (datetime('now')),
                file_size_bytes INTEGER DEFAULT 0,
                file_content_type TEXT DEFAULT '',
                storage_path TEXT,
                uploaded_at TEXT DEFAULT '',
                processed_at TEXT DEFAULT '',
                processing_duration_ms INTEGER DEFAULT 0,
                provider_used TEXT DEFAULT '',
                model_used TEXT,
                page_count INTEGER,
                extraction_metadata TEXT DEFAULT '{}'
            )
            """;
        cmd.ExecuteNonQuery();

        // Migrate existing DBs
        foreach (var (name, def) in NewColumns)
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE documents ADD COLUMN {name} {def}";
                alter.ExecuteNonQuery();
            }
            catch { /* column already exists */ }
        }
    }

    public void Save(DocumentResponse document)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO documents
            (id, filename, document_type, document_subtype, title, confidence, content, quality,
             validation, raw_text, file_size_bytes, file_content_type, storage_path,
             uploaded_at, processed_at, processing_duration_ms, provider_used, model_used,
             page_count, extraction_metadata)
            VALUES ($id,$filename,$docType,$docSubtype,$title,$confidence,$content,$quality,
                    $validation,$rawText,$fileSize,$contentType,$storagePath,
                    $uploadedAt,$processedAt,$durationMs,$provider,$model,$pageCount,$metadata)
            """;
        cmd.Parameters.AddWithValue("$id", document.Id);
        cmd.Parameters.AddWithValue("$filename", document.Filename ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$docType", document.DocumentType);
        cmd.Parameters.AddWithValue("$docSubtype", document.DocumentSubtype ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$title", document.Title);
        cmd.Parameters.AddWithValue("$confidence", document.Confidence);
        cmd.Parameters.AddWithValue("$content", JsonSerializer.Serialize(document.Content));
        cmd.Parameters.AddWithValue("$quality", document.Quality != null ? JsonSerializer.Serialize(document.Quality) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$validation", document.Validation != null ? JsonSerializer.Serialize(document.Validation) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$rawText", document.RawText ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$fileSize", document.FileSizeBytes);
        cmd.Parameters.AddWithValue("$contentType", document.FileContentType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$storagePath", document.StoragePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$uploadedAt", document.UploadedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$processedAt", document.ProcessedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$durationMs", document.ProcessingDurationMs);
        cmd.Parameters.AddWithValue("$provider", document.ProviderUsed ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$model", document.ModelUsed ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$pageCount", document.PageCount.HasValue ? (object)document.PageCount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$metadata", document.ExtractionMetadata != null ? JsonSerializer.Serialize(document.ExtractionMetadata) : "{}");
        cmd.ExecuteNonQuery();
    }

    public DocumentResponse? Get(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM documents WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    public IReadOnlyList<DocumentResponse> ListAll(int limit = 100, int offset = 0, string? documentType = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        if (documentType is not null)
        {
            cmd.CommandText = "SELECT * FROM documents WHERE document_type = $type ORDER BY created_at DESC LIMIT $limit OFFSET $offset";
            cmd.Parameters.AddWithValue("$type", documentType);
        }
        else
        {
            cmd.CommandText = "SELECT * FROM documents ORDER BY created_at DESC LIMIT $limit OFFSET $offset";
        }

        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var results = new List<DocumentResponse>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadRow(reader));
        return results.AsReadOnly();
    }

    public bool Delete(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static DocumentResponse ReadRow(SqliteDataReader reader)
    {
        var contentJson = reader["content"]?.ToString() ?? "{}";
        var qualityJson = reader["quality"] as string;
        var validationJson = reader["validation"] as string;
        var metadataJson = reader["extraction_metadata"] as string ?? "{}";

        int? pageCount = null;
        if (reader["page_count"] is not DBNull && reader["page_count"] != null)
            pageCount = Convert.ToInt32(reader["page_count"]);

        return new DocumentResponse
        {
            Id = reader["id"].ToString()!,
            Filename = reader["filename"] as string,
            FileSizeBytes = reader["file_size_bytes"] is not DBNull ? Convert.ToInt64(reader["file_size_bytes"]) : 0,
            FileContentType = reader["file_content_type"] as string,
            StoragePath = reader["storage_path"] as string,
            UploadedAt = reader["uploaded_at"] as string,
            ProcessedAt = reader["processed_at"] as string,
            ProcessingDurationMs = reader["processing_duration_ms"] is not DBNull ? Convert.ToInt64(reader["processing_duration_ms"]) : 0,
            ProviderUsed = reader["provider_used"] as string,
            ModelUsed = reader["model_used"] as string,
            PageCount = pageCount,
            DocumentType = reader["document_type"].ToString()!,
            DocumentSubtype = reader["document_subtype"] as string,
            Title = reader["title"].ToString()!,
            Confidence = Convert.ToDouble(reader["confidence"]),
            Content = JsonSerializer.Deserialize<Dictionary<string, object>>(contentJson) ?? [],
            Quality = qualityJson != null ? JsonSerializer.Deserialize<DocumentQuality>(qualityJson) : null,
            Validation = validationJson != null ? JsonSerializer.Deserialize<DocumentValidation>(validationJson) : null,
            RawText = reader["raw_text"] as string,
            ExtractionMetadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson),
        };
    }
}
