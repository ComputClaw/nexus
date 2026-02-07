using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nexus.Ingest.Models;
using Nexus.Ingest.Services;

namespace Nexus.Ingest.Functions;

/// <summary>
/// Queue trigger: fetches full email from Graph API, applies whitelist logic, writes to table.
/// </summary>
public sealed class EmailProcessorFunction
{
    private readonly GraphService _graphService;
    private readonly EmailIngestionService _emailIngestionService;
    private readonly ILogger<EmailProcessorFunction> _logger;

    public EmailProcessorFunction(
        GraphService graphService,
        EmailIngestionService emailIngestionService,
        ILogger<EmailProcessorFunction> logger)
    {
        _graphService = graphService;
        _emailIngestionService = emailIngestionService;
        _logger = logger;
    }

    [Function("EmailProcessor")]
    public async Task Run(
        [QueueTrigger("email-ingest", Connection = "StorageConnectionString")]
        string messageText,
        CancellationToken ct)
    {
        var queueMsg = JsonSerializer.Deserialize<EmailQueueMessage>(messageText);
        if (queueMsg == null)
        {
            _logger.LogError("Failed to deserialize email queue message");
            return;
        }

        _logger.LogInformation("Processing email queue message: {ChangeType} {Id}",
            queueMsg.ChangeType, queueMsg.ResourceId);

        // Fetch full message from Graph
        var message = await _graphService.FetchMessage(queueMsg.ResourcePath, ct);
        if (message == null)
        {
            _logger.LogWarning("Message {Id} not found in Graph (deleted?). Skipping.",
                queueMsg.ResourceId);
            return;
        }

        // Process through email ingestion service
        await _emailIngestionService.Process(message, queueMsg.ChangeType, ct);
    }
}
