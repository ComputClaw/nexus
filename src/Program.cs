using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using Nexus.Ingest.Services;

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

        // Typed HttpClient for Fireflies (IHttpClientFactory manages lifecycle)
        var firefliesKey = config["Fireflies:ApiKey"];
        if (!string.IsNullOrEmpty(firefliesKey))
        {
            services.AddHttpClient<FirefliesService>(client =>
            {
                client.BaseAddress = new Uri("https://api.fireflies.ai/graphql");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", firefliesKey);
            });
        }

        // Application services
        services.AddSingleton<BlobStorageService>();
        services.AddSingleton<GraphService>();
        services.AddSingleton<SimpleIngestionService>();
        services.AddSingleton<SubscriptionService>();
        
        // Feed management services
        services.AddSingleton<FeedManagementService>();
        
        // HTTP client for feed fetching
        services.AddHttpClient<AtomFeedService>();
        
        // Legacy services (TODO: Remove after migration)
        services.AddSingleton<WhitelistService>();
        services.AddSingleton<EmailIngestionService>();
        services.AddSingleton<CalendarIngestionService>();
        services.AddSingleton<MeetingIngestionService>();
    })
    .Build();

// Initialize services at startup (once, not per-request)
var blobService = host.Services.GetRequiredService<BlobStorageService>();
await blobService.InitializeAsync();

var feedManagementService = host.Services.GetRequiredService<FeedManagementService>();
await feedManagementService.InitializeAsync();

await host.RunAsync();
