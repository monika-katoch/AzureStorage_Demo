using Azure;
using Azure.Data.Tables;

namespace blob_demo.Models;

/// <summary>
/// A row in OrdersTable. FR-1.2: PartitionKey = CustomerRegion (distributes writes,
/// avoids a hot partition), RowKey = OrderId (unique point-lookup key).
/// </summary>
public class OrderEntity : ITableEntity
{
    // ITableEntity contract
    public string PartitionKey { get; set; } = string.Empty; // CustomerRegion
    public string RowKey { get; set; } = string.Empty;       // OrderId (GUID)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }                           // optimistic concurrency

    // Domain columns
    public string CustomerName { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public double Total { get; set; }

    /// <summary>"Pending" -> "Processed". Enables idempotent workers.</summary>
    public string Status { get; set; } = "Pending";

    public string Region => PartitionKey;
    public string OrderId => RowKey;
}

/// <summary>The JSON payload enqueued for the background worker.</summary>
public record OrderMessage(string OrderId, string Region);

/// <summary>Backing model for Orders/Index.</summary>
public class OrderPageViewModel
{
    public IReadOnlyList<OrderEntity> Orders { get; set; } = new List<OrderEntity>();
    public string QueueName { get; set; } = string.Empty;
    public int ApproximateQueueDepth { get; set; }
}
