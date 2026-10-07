using Azure;
using Azure.Data.Tables;
using blob_demo.Models;
using Microsoft.Extensions.Options;

namespace blob_demo.Services;

public interface IOrderTableStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task InsertAsync(OrderEntity order, CancellationToken ct = default);
    Task<OrderEntity?> GetAsync(string region, string orderId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderEntity>> QueryAsync(string? region, CancellationToken ct = default);

    /// <summary>Marks an order Processed using its ETag (optimistic concurrency).</summary>
    Task<bool> MarkProcessedAsync(OrderEntity order, CancellationToken ct = default);
}

public class OrderTableStore : IOrderTableStore
{
    private readonly TableClient _table;
    private readonly ILogger<OrderTableStore> _logger;

    public OrderTableStore(IOptions<OrderPipelineOptions> options, ILogger<OrderTableStore> logger)
    {
        var opts = options.Value;
        _logger = logger;
        _table = new TableClient(opts.ConnectionString, opts.TableName);
    }

    // FR-1.1
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _table.CreateIfNotExistsAsync(ct);
        _logger.LogInformation("Table '{Table}' is ready.", _table.Name);
    }

    public async Task InsertAsync(OrderEntity order, CancellationToken ct = default)
    {
        // AddEntity fails with 409 if the RowKey already exists — protects RowKey uniqueness (FR-1.2).
        await _table.AddEntityAsync(order, ct);
    }

    public async Task<OrderEntity?> GetAsync(string region, string orderId, CancellationToken ct = default)
    {
        try
        {
            // FR-1.3 / partition-scan best practice: point lookup on both keys.
            var response = await _table.GetEntityAsync<OrderEntity>(region, orderId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<OrderEntity>> QueryAsync(string? region, CancellationToken ct = default)
    {
        // FR-1.3: OData filter. Filtering by PartitionKey stays a fast partition query;
        // with no region it degrades to a full scan (acceptable for a demo listing).
        string? filter = string.IsNullOrWhiteSpace(region)
            ? null
            : TableClient.CreateQueryFilter($"PartitionKey eq {region}");

        var results = new List<OrderEntity>();
        await foreach (var e in _table.QueryAsync<OrderEntity>(filter, cancellationToken: ct))
            results.Add(e);

        return results.OrderByDescending(o => o.Timestamp).ToList();
    }

    public async Task<bool> MarkProcessedAsync(OrderEntity order, CancellationToken ct = default)
    {
        order.Status = "Processed";
        try
        {
            // Merge with the entity's ETag: a background change raises 412 and we skip.
            await _table.UpdateEntityAsync(order, order.ETag, TableUpdateMode.Merge, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            _logger.LogWarning("ETag conflict marking order {OrderId} processed; skipping.", order.RowKey);
            return false;
        }
    }
}
