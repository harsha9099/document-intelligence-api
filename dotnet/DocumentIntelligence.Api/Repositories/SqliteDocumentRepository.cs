using System.Text.Json;
using DocumentIntelligence.Api.Models;
using Microsoft.Data.Sqlite;

namespace DocumentIntelligence.Api.Repositories;

public class SqliteDocumentRepository : IDocumentRepository
{
    private readonly string _connectionString;

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
                created_at TEXT DEFAULT (datetime('now'))
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public void Save(DocumentResponse document)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO documents
            (id, filename, document_type, document_subtype, title, confidence, content, quality, validation, raw_text)
            VALUES ($id, $filename, $docType, $docSubtype, $title, $confidence, $content, $quality, $validation, $rawText)
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

        var content = JsonSerializer.Deserialize<Dictionary<string, object>>(contentJson) ?? [];
        var quality = qualityJson != null ? JsonSerializer.Deserialize<DocumentQuality>(qualityJson) : null;
        var validation = validationJson != null ? JsonSerializer.Deserialize<DocumentValidation>(validationJson) : null;

        return new DocumentResponse
        {
            Id = reader["id"].ToString()!,
            Filename = reader["filename"] as string,
            DocumentType = reader["document_type"].ToString()!,
            DocumentSubtype = reader["document_subtype"] as string,
            Title = reader["title"].ToString()!,
            Confidence = Convert.ToDouble(reader["confidence"]),
            Content = content,
            Quality = quality,
            Validation = validation,
            RawText = reader["raw_text"] as string,
        };
    }
}
