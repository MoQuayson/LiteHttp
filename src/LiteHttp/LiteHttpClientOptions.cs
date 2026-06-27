using System;
using System.Collections.Generic;
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

    /// <summary>Number of automatic retries on transient failures (0 = no retry).</summary>
    public int DefaultMaxRetries { get; init; } = 3;

    /// <summary>Whether the handler should manage a cookie container.</summary>
    public bool UseCookies { get; init; } = false;

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
