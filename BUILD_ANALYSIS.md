# LiteHttp — Build Analysis: Pros & Cons

## Pros

**Correct `HttpClient` lifecycle** — Using a singleton `SocketsHttpHandler` with `PooledConnectionLifetime` avoids the two classic traps: socket exhaustion from creating `new HttpClient()` per request, and DNS staleness from never recycling connections.

**Allocation discipline is real** — Stream-based deserialization, `JsonSerializer.SerializeToUtf8Bytes`, `ResponseHeadersRead` for streaming, and `Random.Shared` for jitter are meaningful wins under load. Not cargo-culted.

**Retry algorithm is correct** — Full jitter exponential back-off (`rand(0, min(cap, base * 2^attempt))`) avoids thundering herd, which a naive fixed-delay retry would cause.

**Zero core dependencies** — The library adds only `Microsoft.Extensions.Resilience` (and its transitive chain). No version pinning headaches for consumers that don't use DI.

**Ergonomic API** — The `configure` delegate pattern for `RequestOptions` keeps auth stateless per-request and composes cleanly without polluting client state.

---

## Cons (original sample-codes issues — all resolved in LiteHttp)

### Bugs

✅ **Retry silently drops the request body** (`sample-codes/LightHttpClient.cs:247`)  
**Fixed in** `src/LiteHttp/LiteHttpClient.cs` — `ExecuteWithRetryAsync` now accepts a `Func<HttpContent?>` factory. JSON payloads are serialized to `byte[]` once via `JsonSerializer.SerializeToUtf8Bytes`; raw bodies are buffered via `ReadAsByteArrayAsync` when retries > 0. Each retry attempt gets a fresh `ByteArrayContent` from the factory.

✅ **`Action<LightHttpClientOptions>` DI overload is broken** (`sample-codes/ServiceCollectionExtensions.cs:34-38`)  
**Fixed in** `src/LiteHttp/Extensions/ServiceCollectionExtensions.cs` — The broken overload and the dead `LightHttpClientOptionsBuilder` shim have been removed. `AddLiteHttpClient` now accepts either no args (defaults) or a pre-built `LiteHttpClientOptions` record directly.

### Design gaps

✅ **Reflection hack in tests is fragile**  
**Fixed** — `LiteHttpClient` now has an `internal` constructor accepting `HttpMessageHandler` directly. Tests use `new LiteHttpClient(new StubHandler(...), options)` with no reflection. Accessible via `InternalsVisibleTo("LiteHttp.Tests")` in `LiteHttp.csproj`.

✅ **No `ILightHttpClient` interface**  
**Fixed** — `src/LiteHttp/ILiteHttpClient.cs` declares the full public API. `LiteHttpClient` implements it. Consumers can mock `ILiteHttpClient` with any mocking framework.

✅ **No observability**  
**Fixed** — `LiteHttpClient` accepts an optional `ILogger<LiteHttpClient>?` (defaults to `NullLogger` — zero cost when omitted). Logs: request dispatched (`Debug`), request completed with status + elapsed (`Debug`), retry attempts with reason (`Warning`), timeout (`Warning`). Elapsed time tracked via `Stopwatch`.

✅ **Timeout ambiguity for callers**  
**Fixed** — `SendCoreAsync` catches `OperationCanceledException`, checks `timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested`, and rethrows as `TimeoutException` with the URL and timeout duration in the message. Covered by `Timeout_Throws_TimeoutException_Not_OperationCanceledException` test.

✅ **No circuit breaker**  
**Fixed** — `AddLiteHttpClientWithResilience` in `ServiceCollectionExtensions.cs` integrates `Microsoft.Extensions.Resilience`. Builds a `ResiliencePipeline<HttpResponseMessage>` with exponential-jitter retry + circuit breaker (50% failure ratio, 10-request minimum, 30s sampling window, 15s break). Custom pipelines supported via `Action<ResiliencePipelineBuilder<HttpResponseMessage>>`. Client's own retry is set to 0 to avoid double-retry.

✅ **Inconsistent error surface**  
**Fixed** — New typed response overloads (`PostJsonAsync<TRequest, TResponse>`, `PutJsonAsync<TRequest, TResponse>`, `PatchJsonAsync<TRequest, TResponse>`) auto-check status and deserialize the response body. Raw `HttpResponseMessage` overloads are preserved for callers that need to inspect headers or status themselves.

---

## Summary

All 8 items (2 bugs + 6 design gaps) from the original analysis are resolved in the LiteHttp project. The transport and allocation decisions from the original sample are preserved; the fixes add correctness, testability, observability, and resilience without breaking the ergonomic API surface.
