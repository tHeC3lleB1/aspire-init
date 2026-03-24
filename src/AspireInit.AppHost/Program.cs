var builder = DistributedApplication.CreateBuilder(args);

// ── Infrastructure ────────────────────────────────────────────────────────────

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("documentdb");

var redis = builder.AddRedis("redis");

var rabbitmq = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

// ── Python Parse Worker ────────────────────────────────────────────────────────

var parseWorker = builder.AddDockerfile("parse-worker", "../parse-worker")
    .WithHttpEndpoint(targetPort: 8000, name: "http");

// ── .NET Services ─────────────────────────────────────────────────────────────

var documentApi = builder.AddProject<Projects.AspireInit_DocumentAPI>("document-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(rabbitmq);

builder.AddProject<Projects.AspireInit_AgentWorker>("agent-worker")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WithReference(parseWorker.GetEndpoint("http"))
    // OpenAI key: set in appsettings.Development.json → OpenAI:ApiKey
    // or via env var: OPENAI__APIKEY
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WaitFor(parseWorker);

builder.Build().Run();
