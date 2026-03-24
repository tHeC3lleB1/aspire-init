using AspireInit.AgentWorker;
using AspireInit.AgentWorker.Agents;
using AspireInit.AgentWorker.Plugins;
using Microsoft.SemanticKernel;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// ── Aspire integrations ───────────────────────────────────────────────────────
builder.AddNpgsqlDataSource("documentdb");
builder.AddRedisClient("redis");
builder.AddRabbitMQClient("messaging");

// ── HTTP client for the Python parse-worker (Aspire service discovery) ────────
builder.Services.AddHttpClient("parse-worker", client =>
{
    // "http://parse-worker" is resolved by Aspire service discovery at runtime
    client.BaseAddress = new Uri("http://parse-worker");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// ── Semantic Kernel ───────────────────────────────────────────────────────────
var openAiApiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException(
        "OpenAI:ApiKey is not configured. " +
        "Copy appsettings.Development.json.example → appsettings.Development.json and fill in your key.");

builder.Services.AddKernel()
    .AddOpenAIChatCompletion(
        modelId: builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini",
        apiKey: openAiApiKey)
    .Plugins.AddFromType<DocumentPlugin>();

builder.Services.AddScoped<DocumentAgent>();

// ── Background worker ─────────────────────────────────────────────────────────
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.Run();
