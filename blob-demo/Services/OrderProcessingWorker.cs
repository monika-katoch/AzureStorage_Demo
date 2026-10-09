using System.Text.Json;
using blob_demo.Models;

namespace blob_demo.Services;

/// <summary>
/// Simulated backend worker (FR-2.3). Continuously drains the order queue:
/// receive -> parse -> process idempotently -> mark Processed -> delete.
/// Handles poison messages, duplicates, and slow-processing invisibility.
/// </summary>
public class OrderProcessingWorker : BackgroundService
{
    private readonly IOrderTableStore _table;
    private readonly IOrderQueueClient _queue;
    private readonly ILogger<OrderProcessingWorker> _logger;

    public OrderProcessingWorker(
        IOrderTableStore table,
        IOrderQueueClient queue,
        ILogger<OrderProcessingWorker> logger)
    {
        _table = table;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Make sure the table + queues exist before we start pulling.
        await _queue.InitializeAsync(stoppingToken);
        await _table.InitializeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _queue.ReceiveAsync(max: 8, ct: stoppingToken);
                if (messages.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                foreach (var msg in messages)
                    await HandleMessageAsync(msg, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker loop error; backing off.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task HandleMessageAsync(Azure.Storage.Queues.Models.QueueMessage msg, CancellationToken ct)
    {
        // Poison-message guard: give up after MaxDequeueCount attempts.
        if (msg.DequeueCount > _queue.MaxDequeueCount)
        {
            await _queue.MoveToPoisonAsync(msg, ct);
            return;
        }

        OrderMessage? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OrderMessage>(msg.Body.ToString());
        }
        catch (JsonException)
        {
            payload = null;
        }

        // Unparseable payloads are poison — dead-letter immediately.
        if (payload is null || string.IsNullOrWhiteSpace(payload.OrderId))
        {
            _logger.LogWarning("Malformed message {Id}; dead-lettering.", msg.MessageId);
            await _queue.MoveToPoisonAsync(msg, ct);
            return;
        }

        var order = await _table.GetAsync(payload.Region, payload.OrderId, ct);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found in table; dropping message.", payload.OrderId);
            await _queue.DeleteAsync(msg, ct);
            return;
        }

        // Idempotency (at-least-once delivery): if already Processed, just delete.
        if (string.Equals(order.Status, "Processed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Order {OrderId} already processed; discarding duplicate.", payload.OrderId);
            await _queue.DeleteAsync(msg, ct);
            return;
        }

        // Simulate slow backend work (billing / email receipt). If it approaches the
        // visibility timeout, extend the lease so no other worker grabs the message.
        await SimulateProcessingAsync(msg, ct);

        var updated = await _table.MarkProcessedAsync(order, ct);
        if (updated)
        {
            await _queue.DeleteAsync(msg, ct);
            _logger.LogInformation("Processed order {OrderId} ({Region}).", order.OrderId, order.Region);
        }
        // If the ETag conflicted, leave the message; it will reappear and retry.
    }

    private async Task SimulateProcessingAsync(Azure.Storage.Queues.Models.QueueMessage msg, CancellationToken ct)
    {
        // Pretend work takes ~1.5s. For genuinely long work, this is where you'd call
        // ExtendInvisibilityAsync before the timeout window closes.
        const int workSeconds = 2;
        if (workSeconds >= _queue.VisibilityTimeoutSeconds - 5)
        {
            await _queue.ExtendInvisibilityAsync(
                msg, TimeSpan.FromSeconds(_queue.VisibilityTimeoutSeconds + workSeconds), ct);
        }
        await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
    }
}
