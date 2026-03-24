using System.ComponentModel;
using System.Net.Http.Json;
using Microsoft.SemanticKernel;
using Npgsql;
using StackExchange.Redis;

namespace AspireInit.AgentWorker.Plugins;

/// <summary>
/// Kernel functions available to the document-processing agent.
/// The agent calls these for all I/O; the LLM itself handles classify/extract/summarize.
/// </summary>
public sealed class DocumentPlugin(
    NpgsqlDataSource dataSource,
    IConnectionMultiplexer redis,
    IHttpClientFactory httpClientFactory,
    ILogger<DocumentPlugin> logger)
{
    // ── Parse ─────────────────────────────────────────────────────────────────

    [KernelFunction("ParseDocument")]
    [Description("Fetches the document from the database and calls the parse service to extract plain text. Returns the extracted text.")]
    public async Task<string> ParseDocumentAsync(
        [Description("The document ID (UUID string) to parse")] string documentId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("ParseDocument called for {DocumentId}", documentId);

        // Load file bytes from PostgreSQL
        byte[] fileBytes;
        string contentType;

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT file_bytes, content_type FROM documents WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", Guid.Parse(documentId));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return $"ERROR: Document {documentId} not found.";

        fileBytes = (byte[])reader["file_bytes"];
        contentType = (string)reader["content_type"];
        await reader.CloseAsync();

        // Call the Python parse-worker
        var client = httpClientFactory.CreateClient("parse-worker");
        var payload = new
        {
            file_bytes_base64 = Convert.ToBase64String(fileBytes),
            content_type = contentType
        };

        var response = await client.PostAsJsonAsync("/parse", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"ERROR: Parse service returned {response.StatusCode}.";

        var result = await response.Content.ReadFromJsonAsync<ParseResponse>(cancellationToken: cancellationToken);
        return result?.Text ?? string.Empty;
    }

    // ── Store Result ──────────────────────────────────────────────────────────

    [KernelFunction("StoreResult")]
    [Description("Persists the final analysis result (document type, summary, entities, raw text) to the database and marks the document as Completed.")]
    public async Task<string> StoreResultAsync(
        [Description("The document ID (UUID string)")] string documentId,
        [Description("The classified document type, e.g. Invoice, Contract, Report, Letter, Form, Other")] string documentType,
        [Description("A 2–3 sentence summary of the document")] string summary,
        [Description("JSON object with keys: people, organizations, locations, dates, amounts — each an array of strings")] string entitiesJson,
        [Description("The full raw text extracted from the document")] string rawText,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("StoreResult called for {DocumentId} — type: {Type}", documentId, documentType);

        var docId = Guid.Parse(documentId);
        var resultId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

        // Insert result
        await using var insertCmd = new NpgsqlCommand("""
            INSERT INTO document_results (id, document_id, document_type, summary, entities_json, raw_text, created_at)
            VALUES (@id, @docId, @type, @summary, @entities::jsonb, @rawText, @now)
            """, conn);
        insertCmd.Parameters.AddWithValue("id", resultId);
        insertCmd.Parameters.AddWithValue("docId", docId);
        insertCmd.Parameters.AddWithValue("type", documentType);
        insertCmd.Parameters.AddWithValue("summary", summary);
        insertCmd.Parameters.AddWithValue("entities", entitiesJson);
        insertCmd.Parameters.AddWithValue("rawText", rawText);
        insertCmd.Parameters.AddWithValue("now", now);
        await insertCmd.ExecuteNonQueryAsync(cancellationToken);

        // Mark document as Completed
        await using var updateCmd = new NpgsqlCommand("""
            UPDATE documents SET status = 'Completed', updated_at = @now WHERE id = @id
            """, conn);
        updateCmd.Parameters.AddWithValue("now", now);
        updateCmd.Parameters.AddWithValue("id", docId);
        await updateCmd.ExecuteNonQueryAsync(cancellationToken);

        // Update Redis cache
        var cache = redis.GetDatabase();
        await cache.StringSetAsync($"doc:{documentId}:status", "Completed", TimeSpan.FromHours(24));

        return $"Result stored successfully. ResultId: {resultId}";
    }

    // ── Update Status ─────────────────────────────────────────────────────────

    [KernelFunction("UpdateDocumentStatus")]
    [Description("Updates the processing status of a document. Use status=Processing when starting, status=Failed if an unrecoverable error occurs.")]
    public async Task<string> UpdateDocumentStatusAsync(
        [Description("The document ID (UUID string)")] string documentId,
        [Description("New status: Processing or Failed")] string status,
        [Description("Optional error message if status is Failed")] string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("UpdateDocumentStatus: {DocumentId} → {Status}", documentId, status);

        var docId = Guid.Parse(documentId);
        var now = DateTime.UtcNow;

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand("""
            UPDATE documents SET status = @status, error_message = @error, updated_at = @now WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("error", (object?)errorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("id", docId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        var cache = redis.GetDatabase();
        await cache.StringSetAsync($"doc:{documentId}:status", status, TimeSpan.FromHours(24));

        return $"Status updated to {status}.";
    }

    private record ParseResponse(string Text);
}
