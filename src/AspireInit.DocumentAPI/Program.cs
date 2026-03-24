using AspireInit.DocumentAPI.Data;
using AspireInit.DocumentAPI.Endpoints;
using AspireInit.DocumentAPI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Aspire integrations ───────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<AppDbContext>("documentdb");
builder.AddRedisClient("redis");
builder.AddRabbitMQClient("messaging");

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddScoped<DocumentPublisher>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Ensure schema exists on startup (EnsureCreated is fine for a PoC)
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapDocumentEndpoints();

app.Run();
