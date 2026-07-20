# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] - 2026-07-20

### Added

- Core `LiteHttpClient` — a thread-safe, allocation-minimal HTTP client built on `SocketsHttpHandler`, designed for long-lived singleton usage.
- Typed JSON send/receive helpers (`GetJsonAsync`, `PostJsonAsync`, `PutJsonAsync`, `PatchJsonAsync`) via `System.Text.Json`.
- Automatic retry with exponential backoff and full jitter for transient status codes (408, 429, 500, 502, 503, 504) and exceptions (`HttpRequestException`, `TimeoutException`, `IOException`), honoring a server-supplied `Retry-After` header when present.
- Per-request timeout, headers, bearer token, basic auth, and API key overrides via a fluent `RequestOptions` API.
- Streaming support via `StreamLinesAsync` (`IAsyncEnumerable<string>`) with zero full-body buffering.
- Form-urlencoded, multipart/form-data, and raw multipart (mixed, related, …) helpers.
- Dependency injection support: singleton and named-client registration, `ILiteHttpClientFactory`.
- Optional Polly resilience pipeline (retry + circuit breaker) via `AddLiteHttpClientWithResilience` / `AddNamedLiteHttpClientWithResilience`.
- `LiteHttpRequestException`, carrying `StatusCode` and `ResponseBody`, thrown by the typed JSON overloads and `StreamLinesAsync` on a non-success status.

### Fixed

- DI registration no longer requires consumers to call `AddLogging()` — the logger is now resolved optionally, matching `LiteHttpClient`'s own logger-less fallback.
- Trace-level header logging now redacts sensitive header values (`Authorization`, `X-Api-Key`, `Cookie`, `Set-Cookie`) instead of logging them in plaintext.
- Public request methods now validate `url`, `method`, and form/multipart delegate parameters up front with clear `ArgumentException`/`ArgumentNullException` messages, instead of failing deep in the call stack with unclear exceptions.
- Registering two named clients under the same name now throws immediately at startup instead of silently leaking the earlier client.
- Documented that `DefaultTimeout` (or a per-request `RequestOptions.Timeout`) bounds the entire resilience pipeline execution when a custom Polly pipeline is supplied, including all of its internal retries.

### Packaging

- Added NuGet package metadata: description, tags, project URL, repository type.
- Enabled XML documentation file generation so IntelliSense docs ship with the package.
- Enabled SourceLink and symbol package (`.snupkg`) generation for debugger source stepping.
- Added an explicit `Polly.Core` package reference, since `ResiliencePipeline<T>` is part of the public API surface.
