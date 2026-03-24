var builder = DistributedApplication.CreateBuilder(args);

// ── Infrastructure ────────────────────────────────────────────────────────────

var pgPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: pgPassword)
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
    .WithEnvironment("OpenAI__ApiKey", builder.Configuration["OpenAI:ApiKey"])
    .WithEnvironment("OpenAI__Model", builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini")
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WaitFor(parseWorker)
    .WaitFor(documentApi);

builder.Build().Run();
