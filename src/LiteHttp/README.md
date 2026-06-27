# LiteHttp

A production-grade, allocation-minimal HTTP client wrapper for .NET 8+, built on `SocketsHttpHandler`.

## Features

- Minimal allocations — wraps a single `HttpClient` + `SocketsHttpHandler` as a thread-safe singleton
- Typed JSON send/receive via `System.Text.Json`
- Automatic retry with exponential backoff and full jitter
- Per-request timeout, headers, bearer token, and API key overrides (fluent API)
- Streaming support via `IAsyncEnumerable<string>` with zero body buffering
- Form-urlencoded, multipart/form-data, and raw multipart (mixed, related, …) helpers
- Dependency injection support — singleton, named clients, and factory pattern
- Optional Polly resilience pipeline (circuit breaker + retry) via `Microsoft.Extensions.Resilience`

## Installation

```csharp
dotnet add package LiteHttp --version 1.0.0-preview.6
```

Build requires .NET 8 SDK or later.

## Quick start

```csharp
using LiteHttp;

// Create once and reuse for the lifetime of the application.
var client = new LiteHttpClient(new LiteHttpClientOptions
{
    BaseAddress       = new Uri("https://api.example.com"),
    DefaultTimeout    = TimeSpan.FromSeconds(15),
    DefaultMaxRetries = 3,
});

// GET + deserialize
var post = await client.GetJsonAsync<Post>("/posts/1");

// POST JSON (raw response)
using var response = await client.PostJsonAsync("/posts", new NewPost(1, "Hello", "Body"));
response.EnsureSuccessStatusCode();

// POST JSON (typed response)
var created = await client.PostJsonAsync<NewPost, Post>("/posts", new NewPost(1, "Title", "Body"));
```

## Configuration

Pass `LiteHttpClientOptions` at construction time. All properties are `init`-only.

| Property | Type | Default | Description |
|---|---|---|---|
| `BaseAddress` | `Uri?` | `null` | Base URI prepended to all relative paths |
| `DefaultTimeout` | `TimeSpan` | 30 s | Request timeout (overridable per-request) |
| `ConnectTimeout` | `TimeSpan` | 10 s | TCP connection timeout |
| `PooledConnectionLifetime` | `TimeSpan` | 5 min | DNS refresh interval for pooled connections |
| `PooledConnectionIdleTimeout` | `TimeSpan` | 2 min | Idle connection eviction threshold |
| `MaxConnectionsPerServer` | `int` | 20 | Max concurrent connections per endpoint |
| `DefaultMaxRetries` | `int` | 3 | Retry attempts per request (0 = no retry) |
| `UseCookies` | `bool` | `false` | Enable cookie container |
| `DefaultHeaders` | `IReadOnlyDictionary<string, string>?` | `null` | Headers sent on every request |
| `JsonSerializerOptions` | `JsonSerializerOptions?` | `null` | Custom JSON serializer options |

## Per-request options

Every method accepts an optional `Action<RequestOptions> configure` delegate for per-request overrides.

| Property | Type | Description |
|---|---|---|
| `Timeout` | `TimeSpan?` | Overrides `DefaultTimeout` for this request |
| `MaxRetries` | `int?` | Overrides `DefaultMaxRetries` for this request |
| `Headers` | `Dictionary<string, string>?` | Additional headers merged for this request |
| `BearerToken` | `string?` | Sets `Authorization: Bearer <token>` |
| `ApiKey` | `string?` | Sets `X-Api-Key: <key>` |

Fluent helpers on `RequestOptions`:

```csharp
// Bearer token
var post = await client.GetJsonAsync<Post>("/posts/1",
    opts => opts.WithBearer("my-jwt-token"));

// API key
var data = await client.GetJsonAsync<Data>("/data",
    opts => opts.WithApiKey("secret-key"));

// Custom header
using var response = await client.SendAsync(HttpMethod.Get, "/endpoint", body: null,
    opts =>
    {
        opts.Timeout    = TimeSpan.FromSeconds(5);
        opts.MaxRetries = 0;
        opts.WithHeader("X-Request-ID", Guid.NewGuid().ToString());
    });
```

## HTTP methods

### GET

```csharp
// Typed response
var post = await client.GetJsonAsync<Post>("/posts/1", ct: cancellationToken);
```

### POST

```csharp
// Raw HttpResponseMessage
using var response = await client.PostJsonAsync("/posts", payload, ct: ct);

// Typed response (calls EnsureSuccessStatusCode internally)
var created = await client.PostJsonAsync<NewPost, Post>("/posts", payload, ct: ct);
```

### PUT / PATCH

```csharp
using var put   = await client.PutJsonAsync("/posts/1", updated, ct: ct);
using var patch = await client.PatchJsonAsync("/posts/1", delta, ct: ct);

// Typed variants
var result = await client.PutJsonAsync<NewPost, Post>("/posts/1", updated, ct: ct);
```

### DELETE

```csharp
using var response = await client.DeleteAsync("/posts/1", ct: ct);
```

### Form POST

```csharp
var fields = new[]
{
    new KeyValuePair<string, string>("title", "Test"),
    new KeyValuePair<string, string>("body",  "Hello"),
};
using var response = await client.PostFormAsync("/submit", fields, ct: ct);
```

### Multipart / file upload

`PostMultipartAsync` has two overloads depending on the content type you need.

**`multipart/form-data`** — builder delegate; the client creates the `MultipartFormDataContent` for you:

```csharp
using var response = await client.PostMultipartAsync("/upload", form =>
{
    form.Add(new StringContent("My title"), "title");
    form.Add(new StreamContent(fileStream), "file", "report.pdf");
}, ct: ct);
```

**Raw `MultipartContent` factory** — full control over the subtype (`mixed`, `related`, or any custom value). The factory is called on each retry attempt to produce a fresh, unread instance:

```csharp
using var response = await client.PostMultipartAsync("/batch", () =>
{
    var content = new MultipartContent("mixed");
    content.Add(new StringContent("{\"op\":\"query\"}", Encoding.UTF8, "application/json"));
    content.Add(new ByteArrayContent(binaryData) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } });
    return content;
}, ct: ct);
```

### Streaming (SSE / NDJSON)

`StreamLinesAsync` reads with `HttpCompletionOption.ResponseHeadersRead` and yields non-empty lines without buffering the full body.

```csharp
await foreach (var line in client.StreamLinesAsync("/events", ct: ct))
{
    Console.WriteLine(line);
}
```

## Dependency injection

```csharp
using LiteHttp.Extensions;

// Single client — resolves as ILiteHttpClient
services.AddLiteHttpClient(new LiteHttpClientOptions
{
    BaseAddress    = new Uri("https://api.example.com"),
    DefaultTimeout = TimeSpan.FromSeconds(10),
});

// Consume in a service
public class MyService(ILiteHttpClient client) { ... }
```

### Named clients

Register multiple upstream services and retrieve them via `ILiteHttpClientFactory`. `ILiteHttpClientFactory` is registered automatically by the first `AddNamedLiteHttpClient` call — do not register it manually.

```csharp
using LiteHttp.Extensions;

services.AddNamedLiteHttpClient("payments", new LiteHttpClientOptions
{
    BaseAddress    = new Uri("https://payments.example.com"),
    DefaultTimeout = TimeSpan.FromSeconds(20),
    DefaultHeaders = new Dictionary<string, string> { ["X-Source"] = "backend-api" },
});

services.AddNamedLiteHttpClient("notifications", new LiteHttpClientOptions
{
    BaseAddress = new Uri("https://notify.example.com"),
});

// Consume in a service
public class OrderService(ILiteHttpClientFactory factory)
{
    private readonly ILiteHttpClient _payments       = factory.GetClient("payments");
    private readonly ILiteHttpClient _notifications  = factory.GetClient("notifications");
}
```

> `ILiteHttpClientFactory` is only registered when at least one named client is added. Calling only `AddLiteHttpClient` and then injecting `ILiteHttpClientFactory` will throw at runtime.

## Resilience (Polly)

Both methods wrap the client in a `ResiliencePipeline<HttpResponseMessage>` built with `Microsoft.Extensions.Resilience`. When active, the client's own `DefaultMaxRetries` is automatically set to `0` to prevent double-retrying.

**Single client** — resolves as `ILiteHttpClient`:

```csharp
services.AddLiteHttpClientWithResilience(new LiteHttpClientOptions
{
    BaseAddress = new Uri("https://api.example.com"),
});
```

**Named clients** — retrieve via `ILiteHttpClientFactory`. Use this when multiple upstream services each need their own resilience pipeline:

```csharp
services.AddNamedLiteHttpClientWithResilience("payments", new LiteHttpClientOptions
{
    BaseAddress    = new Uri("https://payments.example.com"),
    DefaultTimeout = TimeSpan.FromSeconds(20),
});

services.AddNamedLiteHttpClientWithResilience("notifications", new LiteHttpClientOptions
{
    BaseAddress = new Uri("https://notify.example.com"),
});

// Consume in a service
public class OrderService(ILiteHttpClientFactory factory)
{
    private readonly ILiteHttpClient _payments = factory.GetClient("payments");
}
```

Both methods accept an optional `Action<ResiliencePipelineBuilder<HttpResponseMessage>>` to replace the default pipeline:

```csharp
services.AddNamedLiteHttpClientWithResilience("payments", options, pipeline =>
{
    pipeline.AddRetry(new RetryStrategyOptions<HttpResponseMessage> { MaxRetryAttempts = 5 });
});
```

Default circuit breaker parameters:

| Parameter                | Value        |
|--------------------------|--------------|
| Failure ratio threshold  | 50%          |
| Minimum throughput       | 10 requests  |
| Sampling window          | 30 s         |
| Break duration           | 15 s         |

## Retry behavior

The built-in retry loop (used when `DefaultMaxRetries > 0` and no Polly pipeline is active) triggers on:

**Status codes:** 408, 429, 500, 502, 503, 504

**Exceptions:** `HttpRequestException`, `TimeoutException`, `IOException`

Back-off uses exponential delay with full jitter:

```
delay = random(0, min(10 s, 200 ms × 2^attempt))
```

`HttpRequestMessage` is rebuilt on each retry attempt.

## Logging

LiteHttp logs via the standard `ILogger<LiteHttpClient>` abstraction. Log output is controlled by your host's logging configuration — no additional setup required.

| Level | Event |
|-------------|-------|
| `Information` | Request sent (`Sending HTTP request {Method} {Url}`) |
| `Information` | Response received (`Received HTTP response {StatusCode} for {Method} {Url} after {ElapsedMs}ms`) |
| `Warning` | Retry triggered (status code or transient exception) |
| `Warning` | Request timed out |
| `Trace` | Request and response headers (one line per header) |

The `Information` logs fire on every attempt, including retries. The elapsed time in the response log reflects the network round-trip for that attempt only, not the total time including backoff delays.

Header logging at `Trace` is guarded by `IsEnabled` — no string allocation occurs when `Trace` is not enabled.

To enable `Trace` headers in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "LiteHttp.LiteHttpClient": "Trace"
    }
  }
}
```

## Development

```bash
# Build the library
dotnet build src/LiteHttp/

# Build the full solution
dotnet build LiteHttp.sln

# Run all tests
dotnet test tests/LiteHttp.Tests/

# Run a specific test
dotnet test tests/LiteHttp.Tests/ --filter "FullyQualifiedName~Retries_On_503"
```

## Project structure

```
src/
  LiteHttp/                          # Core library (net8.0)
    LiteHttpClient.cs                # Main client implementation
    LiteHttpClientOptions.cs         # LiteHttpClientOptions + RequestOptions
    ILiteHttpClient.cs               # Public interface
    Extensions/
      ServiceCollectionExtensions.cs # DI registration helpers
  LiteHttp.Samples/                  # Runnable usage examples
tests/
  LiteHttp.Tests/                    # xUnit test suite
sample-codes/                        # Historical reference (pre-LiteHttp)
```
