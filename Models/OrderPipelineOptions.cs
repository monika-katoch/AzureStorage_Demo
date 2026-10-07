namespace blob_demo.Models;

/// <summary>Binds the "OrderPipeline" config section (Table + Queue settings).</summary>
public class OrderPipelineOptions
{
    public const string SectionName = "OrderPipeline";

    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string TableName { get; set; } = "OrdersTable";
    public string QueueName { get; set; } = "order-processing-queue";
    public string PoisonQueueName { get; set; } = "order-processing-queue-poison";

    /// <summary>Poison-message threshold before dead-lettering.</summary>
    public int MaxDequeueCount { get; set; } = 5;

    /// <summary>How long a received message stays invisible while a worker processes it.</summary>
    public int VisibilityTimeoutSeconds { get; set; } = 30;
}
