using System.Net;
using Azure.Communication.Email;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NewspaperOrchestrator;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Images;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient();

        // Named HttpClient for fetching article HTML (anti-blocking headers)
        services.AddHttpClient("ArticleFetcher", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "cs,en;q=0.9");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        });

        // OpenAI clients (shared API key, thread-safe singletons)
        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");

        services.AddSingleton(new ChatClient(model: "gpt-4o", apiKey: openAiApiKey));
        services.AddSingleton(new ImageClient(model: "gpt-image-2", apiKey: openAiApiKey));
        services.AddSingleton(new AudioClient(model: "tts-1", apiKey: openAiApiKey));

        // Azure Blob Storage
        var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage environment variable is not set");
        var blobContainerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME")
            ?? throw new InvalidOperationException("BLOB_CONTAINER_NAME environment variable is not set");

        services.AddSingleton(new BlobServiceClient(storageConnectionString));
        services.AddSingleton(new BlobContainerConfig(blobContainerName));

        // Azure Communication Services (email notifications)
        var emailConnectionString = Environment.GetEnvironmentVariable("EMAIL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("EMAIL_CONNECTION_STRING environment variable is not set");
        services.AddSingleton(new EmailClient(emailConnectionString));
    })
    .Build();

host.Run();
