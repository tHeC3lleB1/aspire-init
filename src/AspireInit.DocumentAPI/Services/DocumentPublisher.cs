using System.Text.Json;
using AspireInit.Contracts;
using RabbitMQ.Client;

namespace AspireInit.DocumentAPI.Services;

public sealed class DocumentPublisher(IConnection connection, ILogger<DocumentPublisher> logger)
{
    private const string UploadedQueue = "documents.uploaded";
    private const string ProcessedQueue = "documents.processed";

    public async Task PublishUploadedAsync(DocumentUploadedMessage message, CancellationToken ct = default)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await DeclareQueueAsync(channel, UploadedQueue, ct);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var props = new BasicProperties { Persistent = true };

        await channel.BasicPublishAsync("", UploadedQueue, mandatory: false, basicProperties: props, body: body, cancellationToken: ct);
        logger.LogInformation("Published DocumentUploaded for {DocumentId}", message.DocumentId);
    }

    private static async Task DeclareQueueAsync(IChannel channel, string queue, CancellationToken ct) =>
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
}
