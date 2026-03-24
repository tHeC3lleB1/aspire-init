using AspireInit.Contracts;
using AspireInit.DocumentAPI.Data;
using AspireInit.DocumentAPI.Models;
using AspireInit.DocumentAPI.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AspireInit.DocumentAPI.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents").WithTags("Documents");

        group.MapPost("/", UploadDocument).DisableAntiforgery();
        group.MapGet("/{id:guid}", GetDocument);
        group.MapGet("/{id:guid}/result", GetResult);

        return app;
    }

    // POST /documents — accepts a multipart file upload
    private static async Task<IResult> UploadDocument(
        IFormFile file,
        AppDbContext db,
        DocumentPublisher publisher,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return Results.BadRequest("File is empty.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileBytes = ms.ToArray(),
            Status = DocumentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        // Cache status for fast polling
        var cache = redis.GetDatabase();
        await cache.StringSetAsync($"doc:{document.Id}:status", DocumentStatus.Pending, TimeSpan.FromHours(24));

        // Notify AgentWorker
        await publisher.PublishUploadedAsync(
            new DocumentUploadedMessage(document.Id, document.FileName, document.ContentType), ct);

        return Results.Created($"/documents/{document.Id}",
            new { id = document.Id, status = DocumentStatus.Pending });
    }

    // GET /documents/{id} — returns status (fast, Redis-backed)
    private static async Task<IResult> GetDocument(
        Guid id,
        AppDbContext db,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        // Try Redis first
        var cache = redis.GetDatabase();
        var cachedStatus = await cache.StringGetAsync($"doc:{id}:status");

        if (cachedStatus.HasValue)
            return Results.Ok(new { id, status = cachedStatus.ToString() });

        // Fall back to DB
        var doc = await db.Documents.AsNoTracking()
            .Select(d => new { d.Id, d.FileName, d.Status, d.ErrorMessage, d.CreatedAt })
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        return doc is null ? Results.NotFound() : Results.Ok(doc);
    }

    // GET /documents/{id}/result — returns the full processing result
    private static async Task<IResult> GetResult(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var result = await db.DocumentResults.AsNoTracking()
            .FirstOrDefaultAsync(r => r.DocumentId == id, ct);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
