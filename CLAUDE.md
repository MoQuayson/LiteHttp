# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

**LiteHttp** — a production-grade, allocation-minimal HTTP client wrapper for .NET 8+, built on `SocketsHttpHandler`. Depends on `Microsoft.Extensions.Resilience` (Polly) for the optional resilience pipeline. The `sample-codes/` directory contains the original reference implementation (predates the current solution, kept for historical reference only).

Target namespace: `LiteHttp` (core), `LiteHttp.Extensions` (DI helpers).

## Commands

```bash
# Run all tests
dotnet test tests/LiteHttp.Tests/

# Run a single test by name
dotnet test tests/LiteHttp.Tests/ --filter "FullyQualifiedName~Retries_On_503"

# Build the library
dotnet build src/LiteHttp/

# Build the full solution
dotnet build LiteHttp.sln
```

## Architecture

### Core files

| File | Purpose |
|---|---|
| `src/LiteHttp/LiteHttpClient.cs` | The client itself. Wraps a private `HttpClient` + `SocketsHttpHandler`. Thread-safe singleton. |
| `src/LiteHttp/LiteHttpClientOptions.cs` | Two classes: `LiteHttpClientOptions` (constructor-time, immutable `init` properties) and `RequestOptions` (per-request, mutable, fluent). |
| `src/LiteHttp/ILiteHttpClient.cs` | Public interface for the client. Enables mocking in tests. |
| `src/LiteHttp/Extensions/ServiceCollectionExtensions.cs` | DI registration: `AddLiteHttpClient(options)` for a singleton, `AddNamedLiteHttpClient(name, options)` for multi-client, `AddLiteHttpClientWithResilience(options)` for Polly integration. Exposes `ILiteHttpClientFactory` / `DefaultLiteHttpClientFactory`. |

### Request flow

Every public method ultimately calls `SendAsync(method, url, body, configure, ct)`, which:
1. Builds a `RequestOptions` by invoking the optional `configure` delegate.
2. Creates a linked `CancellationTokenSource` from the per-request or default timeout.
3. Calls `ExecuteWithRetryAsync`, which loops until success or retries exhausted.

Retried status codes: 408, 429, 500, 502, 503, 504. Retried exceptions: `HttpRequestException`, `TimeoutException`, `IOException`. Back-off is exponential with full jitter (base 200 ms, cap 10 s).

`HttpRequestMessage` must be rebuilt on each retry attempt — `HttpRequestMessage` cannot be resent after the first send.

### Streaming

`StreamLinesAsync` uses `HttpCompletionOption.ResponseHeadersRead` and a `StreamReader` to yield non-empty lines as `IAsyncEnumerable<string>` without buffering the full body.

### DI / named clients

Named clients are registered as `NamedClient` records and aggregated by `DefaultLiteHttpClientFactory` (keyed by name in a `ConcurrentDictionary`). Retrieve them via `ILiteHttpClientFactory.GetClient(name)`.

### Resilience (Polly)

`AddLiteHttpClientWithResilience` builds a `ResiliencePipeline<HttpResponseMessage>` with exponential-jitter retry + circuit breaker. The client's own `DefaultMaxRetries` is set to 0 when Polly owns the retry to avoid double-retrying.

## Testing approach

Tests live in `tests/LiteHttp.Tests/LiteHttpClientTests.cs` and use **xUnit**. Network calls are avoided entirely:

- `StubHandler` — an `HttpMessageHandler` backed by a delegate, constructed directly via the `internal` constructor on `LiteHttpClient`.
- Unit tests set `DefaultMaxRetries = 0` unless they explicitly exercise retry logic.
- `InternalsVisibleTo("LiteHttp.Tests")` in `LiteHttp.csproj` grants test access to the internal constructor.

Integration tests (not yet written) should exercise the `SocketsHttpHandler` constructor path against a real or local server.
