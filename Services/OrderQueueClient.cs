using System.Text;
using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using blob_demo.Models;
using Microsoft.Extensions.Options;

namespace blob_demo.Services;

public interface IOrderQueueClient
{
    Task InitializeAsync(CancellationToken ct = default);
    Task EnqueueAsync(OrderMessage message, CancellationToken ct = default);

    Task<QueueMessage[]> ReceiveAsync(int max, CancellationToken ct = default);
    Task DeleteAsync(QueueMessage message, CancellationToken ct = default);
    Task ExtendInvisibilityAsync(QueueMessage message, TimeSpan visibility, CancellationToken ct = default);
    Task MoveToPoisonAsync(QueueMessage message, CancellationToken ct = default);

    Task<int> ApproximateDepthAsync(CancellationToken ct = default);
    int VisibilityTimeoutSeconds { get; }
    int MaxDequeueCount { get; }
}

public class OrderQueueClient : IOrderQueueClient
{
    private const int MaxPayloadBytes = 64 * 1024; // NFR-2.1: 64 KB per message

    private readonly QueueClient _queue;
    private readonly QueueClient _poison;
    private readonly OrderPipelineOptions _opts;
    private readonly ILogger<OrderQueueClient> _logger;

    public int VisibilityTimeoutSeconds => _opts.VisibilityTimeoutSeconds;
    public int MaxDequeueCount => _opts.MaxDequeueCount;

    public OrderQueueClient(IOptions<OrderPipelineOptions> options, ILogger<OrderQueueClient> logger)
    {
        _opts = options.Value;
        _logger = logger;

        // NFR-1.1: the SDK Base64-encodes on send and decodes on receive.
        var clientOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        _queue = new QueueClient(_opts.ConnectionString, _opts.QueueName, clientOptions);
        _poison = new QueueClient(_opts.ConnectionString, _opts.PoisonQueueName, clientOptions);
    }

    // FR-2.1
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _queue.CreateIfNotExistsAsync(cancellationToken: ct);
        await _poison.CreateIfNotExistsAsync(cancellationToken: ct);
        _logger.LogInformation("Queues '{Q}' and '{P}' are ready.", _opts.QueueName, _opts.PoisonQueueName);
    }

    // FR-2.2
    public async Task EnqueueAsync(OrderMessage message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaxPayloadBytes)
            throw new InvalidOperationException($"Message ({bytes} B) exceeds the 64 KB queue limit.");

        // Passing the raw JSON string; Base64 encoding is applied by the client.
        await _queue.SendMessageAsync(json, cancellationToken: ct);
    }

    public async Task<QueueMessage[]> ReceiveAsync(int max, CancellationToken ct = default)
    {
        var response = await _queue.ReceiveMessagesAsync(
            maxMessages: max,
            visibilityTimeout: TimeSpan.FromSeconds(_opts.VisibilityTimeoutSeconds),
            cancellationToken: ct);
        return response.Value;
    }

    public Task DeleteAsync(QueueMessage message, CancellationToken ct = default) =>
        _queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, ct);

    // Message-invisibility edge case: extend the lease for slow processing.
    public Task ExtendInvisibilityAsync(QueueMessage message, TimeSpan visibility, CancellationToken ct = default) =>
        _queue.UpdateMessageAsync(message.MessageId, message.PopReceipt, message.Body, visibility, ct);

    // Poison-message edge case: dead-letter then remove from the main queue.
    public async Task MoveToPoisonAsync(QueueMessage message, CancellationToken ct = default)
    {
        await _poison.SendMessageAsync(message.Body.ToString(), cancellationToken: ct);
        await DeleteAsync(message, ct);
        _logger.LogWarning("Message {Id} moved to poison queue after {Count} attempts.",
            message.MessageId, message.DequeueCount);
    }

    public async Task<int> ApproximateDepthAsync(CancellationToken ct = default)
    {
        QueueProperties props = await _queue.GetPropertiesAsync(ct);
        return props.ApproximateMessagesCount;
    }
}
