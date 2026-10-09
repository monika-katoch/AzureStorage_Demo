using blob_demo.Models;

namespace blob_demo.Services;

public record CreateOrderRequest(string Region, string CustomerName, string Product, int Quantity, double Total);

public interface IOrderService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<OrderEntity> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<OrderPageViewModel> GetPageAsync(string? region, CancellationToken ct = default);
}

/// <summary>
/// Fronts the pipeline: persist the order to Table Storage, enqueue its id, and
/// return immediately (the 2.1 diagram). Heavy processing happens in the worker.
/// </summary>
public class OrderService : IOrderService
{
    private readonly IOrderTableStore _table;
    private readonly IOrderQueueClient _queue;

    public OrderService(IOrderTableStore table, IOrderQueueClient queue)
    {
        _table = table;
        _queue = queue;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _table.InitializeAsync(ct);
        await _queue.InitializeAsync(ct);
    }

    public async Task<OrderEntity> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var order = new OrderEntity
        {
            PartitionKey = string.IsNullOrWhiteSpace(request.Region) ? "UNKNOWN" : request.Region.Trim(),
            RowKey = Guid.NewGuid().ToString("N"),
            CustomerName = request.CustomerName,
            Product = request.Product,
            Quantity = request.Quantity,
            Total = request.Total,
            Status = "Pending"
        };

        await _table.InsertAsync(order, ct);
        await _queue.EnqueueAsync(new OrderMessage(order.RowKey, order.PartitionKey), ct);
        return order;
    }

    public async Task<OrderPageViewModel> GetPageAsync(string? region, CancellationToken ct = default)
    {
        return new OrderPageViewModel
        {
            Orders = await _table.QueryAsync(region, ct),
            QueueName = _queue is OrderQueueClient ? "order-processing-queue" : string.Empty,
            ApproximateQueueDepth = await _queue.ApproximateDepthAsync(ct)
        };
    }
}
