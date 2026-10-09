using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FuncQueue_Demo;

public class Function1(ILogger<Function1> logger)
{
    [Function(nameof(Function1))]
    public void Run([QueueTrigger("myqueue", Connection = "")] QueueMessage message)
    {
        logger.LogInformation("C# Queue trigger function processed: {messageText}", message.MessageText);
        
    }
}