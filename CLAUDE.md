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
| `src/LiteHttp/LiteHttpRequestException.cs` | Thrown by the typed JSON overloads and `StreamLinesAsync` on a non-success status. Carries `StatusCode` and `ResponseBody` (unlike the base `HttpRequestException` from `EnsureSuccessStatusCode`). |
| `src/LiteHttp/ILiteHttpClient.cs` | Public interface for the client. Enables mocking in tests. |
| `src/LiteHttp/Extensions/ServiceCollectionExtensions.cs` | DI registration: `AddLiteHttpClient(options)` for a singleton, `AddNamedLiteHttpClient(name, options)` for multi-client, `AddLiteHttpClientWithResilience(options)` for Polly integration. Exposes `ILiteHttpClientFactory` / `DefaultLiteHttpClientFactory`. |

### Request flow

Every public method ultimately calls `SendAsync(method, url, body, configure, ct)`, which:
1. Builds a `RequestOptions` by invoking the optional `configure` delegate.
2. Calls `ExecuteWithRetryAsync`, which loops until success or retries exhausted.
3. On each loop iteration, creates a fresh `CancellationTokenSource` linking the caller's `ct` to a new per-attempt timeout — **each attempt gets its own full timeout window**, not a shared budget across retries. A per-attempt timeout is treated as a transient failure and consumes one retry rather than aborting the whole operation.

Retried status codes: 408, 429, 500, 502, 503, 504. Retried exceptions: `HttpRequestException`, `TimeoutException`, `IOException`. Back-off is exponential with full jitter (base 200 ms, cap 10 s) unless the response carries a `Retry-After` header, in which case that delay is honored instead (capped at `LiteHttpClientOptions.MaxRetryAfterDelay`, default 60 s). All default-retried conditions are safe to retry even for non-idempotent methods (POST/PATCH) since they imply the request never reached application logic — a custom resilience pipeline with broader conditions must preserve that property itself.

`HttpRequestMessage` must be rebuilt on each retry attempt — `HttpRequestMessage` cannot be resent after the first send.

The typed JSON overloads (`GetJsonAsync<T>`, `PostJsonAsync<TReq,TResp>`, `PutJsonAsync<TReq,TResp>`, `PatchJsonAsync<TReq,TResp>`) and `StreamLinesAsync` throw `LiteHttpRequestException` (not the base `EnsureSuccessStatusCode` exception) on a non-success status, so the response body is available on the exception for diagnostics.

### Streaming

`StreamLinesAsync` uses `HttpCompletionOption.ResponseHeadersRead` and a `StreamReader` to yield non-empty lines as `IAsyncEnumerable<string>` without buffering the full body. The caller's `configure` delegate runs *before* `ResponseHeadersRead` is forced to `true`, so streaming can't be silently disabled by a `configure` callback that happens to touch that flag.

### DI / named clients

Named clients are registered as `NamedClient` records and aggregated by `DefaultLiteHttpClientFactory` (keyed by name in a `ConcurrentDictionary`). Retrieve them via `ILiteHttpClientFactory.GetClient(name)`. `DefaultLiteHttpClientFactory` implements `IDisposable` and disposes every named client it holds — required because the DI container only auto-disposes the `NamedClient` record it directly resolves, and that record isn't itself disposable.

### Resilience (Polly)

`AddLiteHttpClientWithResilience` builds a `ResiliencePipeline<HttpResponseMessage>` with exponential-jitter retry + circuit breaker. The client's own `DefaultMaxRetries` is set to 0 when Polly owns the retry to avoid double-retrying.

## Testing approach

Tests live in `tests/LiteHttp.Tests/LiteHttpClientTests.cs` and use **xUnit**. Network calls are avoided entirely:

- `StubHandler` — an `HttpMessageHandler` backed by a delegate, constructed directly via the `internal` constructor on `LiteHttpClient`.
- Unit tests set `DefaultMaxRetries = 0` unless they explicitly exercise retry logic.
- `InternalsVisibleTo("LiteHttp.Tests")` in `LiteHttp.csproj` grants test access to the internal constructor.

Integration tests (not yet written) should exercise the `SocketsHttpHandler` constructor path against a real or local server.
