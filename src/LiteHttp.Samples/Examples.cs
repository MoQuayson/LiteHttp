using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using LiteHttp;
using LiteHttp.Extensions;

// ─── Example models ───────────────────────────────────────────────────────────
record Post(int Id, int UserId, string Title, string Body);
record NewPost(int UserId, string Title, string Body);

// =============================================================================
// 1. STANDALONE — new-up a singleton and reuse it across the app lifetime
// =============================================================================
class StandaloneExample
{
    private static readonly LiteHttpClient _client = new(new LiteHttpClientOptions
    {
        BaseAddress             = new Uri("https://jsonplaceholder.typicode.com"),
        DefaultTimeout          = TimeSpan.FromSeconds(15),
        MaxConnectionsPerServer = 32,
        DefaultMaxRetries       = 3,
    });

    // ── GET + deserialise ────────────────────────────────────────────────────
    public static async Task GetExample(CancellationToken ct)
    {
        var post = await _client.GetJsonAsync<Post>("/posts/1", ct: ct);
        Console.WriteLine($"[GET] {post?.Title}");
    }

    // ── GET with per-request bearer token ────────────────────────────────────
    public static async Task GetWithAuthExample(CancellationToken ct)
    {
        var post = await _client.GetJsonAsync<Post>(
            "/posts/2",
            opts => opts.WithBearer("my-jwt-token"),
            ct);
        Console.WriteLine($"[GET+Auth] {post?.Title}");
    }

    // ── POST JSON (raw response) ──────────────────────────────────────────────
    public static async Task PostExample(CancellationToken ct)
    {
        var payload = new NewPost(1, "Hello from LiteHttp", "Minimal allocations, full control.");
        using var response = await _client.PostJsonAsync("/posts", payload, ct: ct);
        response.EnsureSuccessStatusCode();
        Console.WriteLine($"[POST] Status: {response.StatusCode}");
    }

    // ── POST JSON (typed response — auto-checks status) ───────────────────────
    public static async Task PostTypedExample(CancellationToken ct)
    {
        var payload = new NewPost(1, "Typed response POST", "Deserializes directly.");
        var created = await _client.PostJsonAsync<NewPost, Post>("/posts", payload, ct: ct);
        Console.WriteLine($"[POST Typed] Created id: {created?.Id}");
    }

    // ── PUT ──────────────────────────────────────────────────────────────────
    public static async Task PutExample(CancellationToken ct)
    {
        var updated = new NewPost(1, "Updated title", "Updated body");
        using var response = await _client.PutJsonAsync("/posts/1", updated, ct: ct);
        Console.WriteLine($"[PUT] Status: {response.StatusCode}");
    }

    // ── DELETE ───────────────────────────────────────────────────────────────
    public static async Task DeleteExample(CancellationToken ct)
    {
        using var response = await _client.DeleteAsync("/posts/1", ct: ct);
        Console.WriteLine($"[DELETE] Status: {response.StatusCode}");
    }

    // ── Stream lines (SSE / NDJSON) ──────────────────────────────────────────
    public static async Task StreamExample(CancellationToken ct)
    {
        int lineCount = 0;
        await foreach (var line in _client.StreamLinesAsync("/posts", ct: ct))
        {
            if (++lineCount > 5) break;
            Console.WriteLine($"[STREAM] {line[..Math.Min(60, line.Length)]}…");
        }
    }

    // ── Form POST ────────────────────────────────────────────────────────────
    public static async Task FormExample(CancellationToken ct)
    {
        var fields = new[]
        {
            new KeyValuePair<string, string>("title", "Test"),
            new KeyValuePair<string, string>("body",  "Hello"),
        };
        using var response = await _client.PostFormAsync("/posts", fields, ct: ct);
        Console.WriteLine($"[FORM] Status: {response.StatusCode}");
    }

    // ── Multipart ────────────────────────────────────────────────────────────
    public static async Task MultipartExample(CancellationToken ct)
    {
        using var response = await _client.PostMultipartAsync("/posts", form =>
        {
            form.Add(new StringContent("My title"), "title");
            form.Add(new StringContent("My body"),  "body");
            // Real file upload: form.Add(new StreamContent(fileStream), "file", "report.pdf");
        }, ct: ct);
        Console.WriteLine($"[MULTIPART] Status: {response.StatusCode}");
    }

    // ── Custom timeout + no retry ─────────────────────────────────────────────
    public static async Task CustomTimeoutExample(CancellationToken ct)
    {
        using var response = await _client.SendAsync(
            HttpMethod.Get,
            "/posts/100",
            body: null,
            opts =>
            {
                opts.Timeout    = TimeSpan.FromSeconds(5);
                opts.MaxRetries = 0;
                opts.WithHeader("X-Request-ID", Guid.NewGuid().ToString());
            },
            ct);
        Console.WriteLine($"[CUSTOM] Status: {response.StatusCode}");
    }
}

// =============================================================================
// 2. DEPENDENCY INJECTION — ASP.NET Core / generic host
// =============================================================================
class DiExample
{
    public static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Single client
        services.AddLiteHttpClient(new LiteHttpClientOptions
        {
            BaseAddress    = new Uri("https://api.myservice.com"),
            DefaultTimeout = TimeSpan.FromSeconds(10),
        });

        // Named clients for multiple upstream services
        services.AddNamedLiteHttpClient("payments", new LiteHttpClientOptions
        {
            BaseAddress    = new Uri("https://payments.myservice.com"),
            DefaultTimeout = TimeSpan.FromSeconds(20),
            DefaultHeaders = new Dictionary<string, string>
            {
                ["X-Source"] = "backend-api",
            },
        });

        services.AddNamedLiteHttpClient("notifications", new LiteHttpClientOptions
        {
            BaseAddress    = new Uri("https://notify.myservice.com"),
            DefaultTimeout = TimeSpan.FromSeconds(8),
        });

        return services.BuildServiceProvider();
    }

    public static async Task Run(CancellationToken ct)
    {
        var sp      = BuildServiceProvider();
        var factory = sp.GetRequiredService<ILiteHttpClientFactory>();

        var payments      = factory.GetClient("payments");
        var notifications = factory.GetClient("notifications");

        Console.WriteLine($"[DI] payments client: {payments.GetType().Name}");
        Console.WriteLine($"[DI] notifications client: {notifications.GetType().Name}");
        await Task.CompletedTask;
    }
}

// =============================================================================
// 3. RESILIENCE — circuit breaker + retry via Microsoft.Extensions.Resilience
// =============================================================================
class ResilienceExample
{
    public static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Default: 3 exponential-jitter retries + 50% failure-ratio circuit breaker
        services.AddLiteHttpClientWithResilience(new LiteHttpClientOptions
        {
            BaseAddress = new Uri("https://api.myservice.com"),
        });

        // Custom resilience pipeline
        services.AddNamedLiteHttpClient("custom-resilience", new LiteHttpClientOptions
        {
            BaseAddress       = new Uri("https://api.myservice.com"),
            DefaultMaxRetries = 0, // resilience pipeline owns retry
        });

        return services.BuildServiceProvider();
    }

    public static async Task Run(CancellationToken ct)
    {
        var sp     = BuildServiceProvider();
        var client = sp.GetRequiredService<LiteHttpClient>();

        Console.WriteLine($"[RESILIENCE] client: {client.GetType().Name}");
        await Task.CompletedTask;
    }
}

// =============================================================================
// 4. ENTRY POINT
// =============================================================================
class Program
{
    static async Task Main()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Console.WriteLine("=== LiteHttpClient Demo ===\n");

        await StandaloneExample.GetExample(cts.Token);
        await StandaloneExample.GetWithAuthExample(cts.Token);
        await StandaloneExample.PostExample(cts.Token);
        await StandaloneExample.PostTypedExample(cts.Token);
        await StandaloneExample.PutExample(cts.Token);
        await StandaloneExample.DeleteExample(cts.Token);
        await StandaloneExample.FormExample(cts.Token);
        await StandaloneExample.StreamExample(cts.Token);
        await StandaloneExample.CustomTimeoutExample(cts.Token);
        await DiExample.Run(cts.Token);
        await ResilienceExample.Run(cts.Token);

        Console.WriteLine("\nAll examples complete.");
    }
}
