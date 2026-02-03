using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
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

        // Queue clients (for HTTP functions to enqueue)
        var queueOpts = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        services.AddKeyedSingleton("email-ingest",
            new QueueClient(storageConn, "email-ingest", queueOpts));
        services.AddKeyedSingleton("calendar-ingest",
            new QueueClient(storageConn, "calendar-ingest", queueOpts));
        services.AddKeyedSingleton("meeting-ingest",
            new QueueClient(storageConn, "meeting-ingest", queueOpts));

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
        services.AddSingleton<WhitelistService>();
        services.AddSingleton<BlobStorageService>();
    })
    .Build();

// Initialize blob containers at startup (once, not per-request)
var blobService = host.Services.GetRequiredService<BlobStorageService>();
await blobService.InitializeAsync();

await host.RunAsync();
