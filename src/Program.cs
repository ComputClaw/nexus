using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using Nexus.Ingest.Feeds;
using Nexus.Ingest.Graph;
using Nexus.Ingest.Services;
using Nexus.Ingest.Webhooks.Processors;
using Nexus.Ingest.Webhooks.Relays;
using Nexus.Ingest.Whitelist;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        var storageConn = config["StorageConnectionString"]
            ?? config["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("StorageConnectionString not configured");

        // Table Storage
        services.AddSingleton(new TableServiceClient(storageConn));

        // Blob Storage
        services.AddSingleton(new BlobServiceClient(storageConn));

        // Queue clients (via factory — keyed DI not supported in Azure Functions host)
        services.AddSingleton(new QueueClientFactory(storageConn));

        // Microsoft Graph (client credentials flow)
        var tenantId = config["Graph:TenantId"];
        var clientId = config["Graph:ClientId"];
        var clientSecret = config["Graph:ClientSecret"];
        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId))
        {
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            services.AddSingleton(new GraphServiceClient(credential));
        }

        // Typed HttpClient for Fireflies (always register so DI resolves; no-op if key not set)
        var firefliesKey = config["Fireflies:ApiKey"];
        services.AddHttpClient<FirefliesService>(client =>
        {
            client.BaseAddress = new Uri("https://api.fireflies.ai/graphql");
            if (!string.IsNullOrEmpty(firefliesKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", firefliesKey);
            }
        });

        // Application services
        services.AddSingleton<BlobStorageService>();
        services.AddSingleton<GraphService>();
        services.AddSingleton<IngestionService>();
        services.AddSingleton<SubscriptionService>();

        // Feed management services
        services.AddSingleton<FeedManagementService>();

        // HTTP client for feed fetching
        services.AddHttpClient<AtomFeedService>();

        // Webhook relays
        services.AddSingleton<IWebhookRelay, GraphWebhookRelay>();
        services.AddSingleton<IWebhookRelay, FirefliesWebhookRelay>();
        services.AddSingleton<IWebhookRelay, GitHubWebhookRelay>();
        services.AddSingleton<IWebhookRelay, GenericWebhookRelay>();

        // Webhook processors
        services.AddSingleton<IWebhookProcessor, GraphEmailProcessor>();
        services.AddSingleton<IWebhookProcessor, GraphCalendarProcessor>();
        services.AddSingleton<IWebhookProcessor, FirefliesMeetingProcessor>();
        services.AddSingleton<IWebhookProcessor, GitHubReleaseProcessor>();
        services.AddSingleton<IWebhookProcessor, GenericWebhookProcessor>();

        // Legacy services (TODO: Remove after migration)
        services.AddSingleton<WhitelistService>();
    })
    .Build();

// Initialize services at startup (once, not per-request)
var blobService = host.Services.GetRequiredService<BlobStorageService>();
await blobService.InitializeAsync();

var feedManagementService = host.Services.GetRequiredService<FeedManagementService>();
await feedManagementService.InitializeAsync();

await host.RunAsync();
