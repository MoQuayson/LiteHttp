# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.1.0-preview.261101.2] - 2026-11-01

Prerelease: published as a preview before 1.1.0 so consumers can validate the retry and TLS changes. This supersedes `1.1.0-preview.261101.1`, which was packed from a stale Release build and does not contain the changes below.

### Changed

- **Behavior change:** the built-in retry loop is now method-aware. POST/PATCH (non-idempotent) are retried only on 408, 429, 503 and pre-send connection failures (connect, DNS, TLS handshake, proxy tunnel). They are no longer retried after 500, 502, 504, per-attempt timeouts or mid-flight `IOException`, where the server may already have applied the change. Idempotent methods (GET, HEAD, OPTIONS, PUT, DELETE, TRACE) retry as before. Previously the docs incorrectly described all default-retried conditions as safe for POST/PATCH.

### Added

- `RequestOptions.RetryNonIdempotent` to opt a single POST/PATCH request back in to retrying ambiguous failures (e.g. when it carries an idempotency key).
- Opt-in TLS server certificate validation on `LiteHttpClientOptions`: `TrustedCaCertificates` (private CA roots), `PinnedCertificateSha256` (self-signed leaf pinning; host name still enforced) and `ServerCertificateValidationCallback` (full custom control). Wired into `SocketsHttpHandler.SslOptions`; none is enabled by default and there is no blanket "ignore errors" flag.

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
