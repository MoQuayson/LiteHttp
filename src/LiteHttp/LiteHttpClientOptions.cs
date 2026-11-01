using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LiteHttp;

/// <summary>
/// Immutable configuration applied once at construction time.
/// All fields have production-safe defaults.
/// </summary>
public sealed record LiteHttpClientOptions
{
    /// <summary>All requests are relative to this address when set.</summary>
    public Uri? BaseAddress { get; init; }

    /// <summary>Default per-request timeout. Per-request options can override.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a pooled connection may be reused before being replaced.</summary>
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long an idle pooled connection is kept alive.</summary>
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Max concurrent connections per unique host:port pair.</summary>
    public int MaxConnectionsPerServer { get; init; } = 20;

    /// <summary>TCP connect timeout — distinct from the overall request timeout.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Number of automatic retries on transient failures (0 = no retry). Each attempt gets its own
    /// <see cref="DefaultTimeout"/> window — a slow attempt that times out consumes one retry rather
    /// than exhausting the whole request's time budget.
    /// </summary>
    /// <remarks>
    /// Idempotent methods (GET, HEAD, OPTIONS, PUT, DELETE, TRACE) are retried on status codes 408, 429,
    /// 500, 502, 503, 504 and on <see cref="System.Net.Http.HttpRequestException"/>/<see cref="TimeoutException"/>/<see cref="System.IO.IOException"/>.
    /// Non-idempotent methods (POST, PATCH) are retried only when the server did not process the
    /// request: 408, 429, 503, and connection-level failures that occur before the request is sent
    /// (connect, DNS, TLS handshake, proxy tunnel). After 500/502/504, timeouts or mid-flight I/O errors
    /// the server may already have applied the change, so those are not retried unless
    /// <see cref="RequestOptions.RetryNonIdempotent"/> is set for that request.
    /// </remarks>
    public int DefaultMaxRetries { get; init; } = 3;

    /// <summary>
    /// Upper bound on how long a single retry wait may be when honoring a server's
    /// <c>Retry-After</c> response header (RFC 9110 §10.2.3). Prevents a misbehaving or malicious
    /// server from stalling the client indefinitely. Ignored when the header is absent, in which
    /// case the jittered exponential back-off is used instead.
    /// </summary>
    public TimeSpan MaxRetryAfterDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Whether the handler should manage a cookie container.</summary>
    public bool UseCookies { get; init; } = false;

    /// <summary>
    /// Private CA root certificates to trust for server certificate validation (custom root trust — the OS
    /// trust store is <b>not</b> consulted). The certificate chain and
    /// host name are still fully validated. Opt-in; <c>null</c> (default) uses the OS trust store.
    /// Ignored when <see cref="ServerCertificateValidationCallback"/> is set.
    /// </summary>
    public X509Certificate2Collection? TrustedCaCertificates { get; init; }

    /// <summary>
    /// SHA-256 thumbprints (hex, case-insensitive, optional <c>:</c> separators) of server leaf
    /// certificates to accept — for self-signed certificates. A matching pin tolerates an untrusted
    /// chain but <b>not</b> a host name mismatch. Opt-in; <c>null</c> (default) disables pinning.
    /// When combined with <see cref="TrustedCaCertificates"/>, satisfying either one is sufficient.
    /// Ignored when <see cref="ServerCertificateValidationCallback"/> is set.
    /// </summary>
    public IReadOnlyCollection<string>? PinnedCertificateSha256 { get; init; }

    /// <summary>
    /// Fully custom server certificate validation. When set it <b>replaces</b> all built-in checks,
    /// including <see cref="TrustedCaCertificates"/> and <see cref="PinnedCertificateSha256"/>.
    /// <para>
    /// <b>Security warning:</b> a callback that unconditionally returns <c>true</c> disables TLS
    /// authentication and exposes every request (including credentials) to man-in-the-middle attacks.
    /// Prefer <see cref="TrustedCaCertificates"/> or <see cref="PinnedCertificateSha256"/>.
    /// </para>
    /// </summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }

    /// <summary>Headers merged into every request sent by this client.</summary>
    public IReadOnlyDictionary<string, string>? DefaultHeaders { get; init; }

    /// <summary>JSON serialisation options reused across all requests (avoids re-creating metadata).</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; init; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>
/// Per-request overrides. Apply via the <c>configure</c> delegate on each Send/Get/Post method.
/// </summary>
public sealed class RequestOptions
{
    /// <summary>Override the default client timeout for this single request.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Additional headers merged into this request only.</summary>
    public Dictionary<string, string>? Headers { get; private set; }

    /// <summary>Convenience: adds/sets a Bearer token on the Authorization header.</summary>
    public string? BearerToken { get; set; }

    /// <summary>Convenience: adds/sets an X-Api-Key header.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Convenience: adds/sets a Basic auth credential on the Authorization header.</summary>
    public (string Username, string Password)? BasicCredentials { get; set; }

    /// <summary>
    /// When true, <see cref="HttpCompletionOption.ResponseHeadersRead"/> is used so the
    /// response body is not buffered — callers must read the stream themselves.
    /// Automatically set when using <c>StreamLinesAsync</c>.
    /// </summary>
    public bool ResponseHeadersRead { get; set; }

    /// <summary>Override the client-level retry count for this request (0 = no retry).</summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Allows POST/PATCH (and other non-idempotent methods) to be retried after failures where the
    /// server may already have applied the request: 500, 502, 504, timeouts and mid-flight I/O errors.
    /// Off by default. Enable only when the call is safe to repeat (e.g. it carries an idempotency key).
    /// </summary>
    public bool RetryNonIdempotent { get; set; }

    /// <summary>Fluent helper — adds a header to this request.</summary>
    public RequestOptions WithHeader(string name, string value)
    {
        Headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Headers[name] = value;
        return this;
    }

    /// <summary>Fluent helper — sets Basic auth credentials.</summary>
    public RequestOptions WithBasic(string username, string password)
    {
        BasicCredentials = (username, password);
        return this;
    }

    /// <summary>Fluent helper — sets the Bearer token.</summary>
    public RequestOptions WithBearer(string token)
    {
        BearerToken = token;
        return this;
    }

    /// <summary>Fluent helper — sets the API key.</summary>
    public RequestOptions WithApiKey(string key)
    {
        ApiKey = key;
        return this;
    }
}
