using System.Text.Json;
using AspireInit.AgentWorker.Agents;
using AspireInit.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AspireInit.AgentWorker;

public sealed class Worker(
    IConnection connection,
    DocumentAgent agent,
    ILogger<Worker> logger) : BackgroundService
{
    private const string UploadedQueue = "documents.uploaded";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started, waiting for messages on '{Queue}'", UploadedQueue);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            UploadedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Process one document at a time — keeps the PoC observable
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            DocumentUploadedMessage? message = null;
            try
            {
                message = JsonSerializer.Deserialize<DocumentUploadedMessage>(ea.Body.Span);
                if (message is null)
                {
                    logger.LogWarning("Received null or unreadable message — discarding.");
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                logger.LogInformation("Received DocumentUploaded: {DocumentId}", message.DocumentId);
                await agent.ProcessAsync(message, stoppingToken);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process document {DocumentId}",
                    message?.DocumentId.ToString() ?? "unknown");
                // Requeue=false → moves to dead-letter if configured, otherwise discards
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await channel.BasicConsumeAsync(UploadedQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        // Keep the worker alive until the host shuts down
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
