using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace LiteHttp;

/// <summary>
/// A lightweight, allocation-minimal HTTP client built on top of SocketsHttpHandler.
/// Thread-safe and designed for long-lived singleton usage — reuse one instance per base address.
/// </summary>
public sealed class LiteHttpClient : ILiteHttpClient, IDisposable
{
    private readonly HttpClient _inner;
    private readonly LiteHttpClientOptions _options;
    private readonly ILogger<LiteHttpClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage>? _resiliencePipeline;
    private bool _disposed;

    public LiteHttpClient(
        LiteHttpClientOptions? options = null,
        ILogger<LiteHttpClient>? logger = null,
        ResiliencePipeline<HttpResponseMessage>? resiliencePipeline = null)
        : this(CreateHandler(options ?? new LiteHttpClientOptions()), options, logger, resiliencePipeline)
    {
    }

    internal LiteHttpClient(
        HttpMessageHandler handler,
        LiteHttpClientOptions? options = null,
        ILogger<LiteHttpClient>? logger = null,
        ResiliencePipeline<HttpResponseMessage>? resiliencePipeline = null)
    {
        _options = options ?? new LiteHttpClientOptions();
        _logger = logger ?? NullLogger<LiteHttpClient>.Instance;
        _resiliencePipeline = resiliencePipeline;

        _inner = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = _options.BaseAddress,
            Timeout     = System.Threading.Timeout.InfiniteTimeSpan, // managed per-request via CTS
        };

        _inner.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
        _inner.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        if (_options.DefaultHeaders is { Count: > 0 })
            foreach (var (k, v) in _options.DefaultHeaders)
                _inner.DefaultRequestHeaders.TryAddWithoutValidation(k, v);
    }

    private static SocketsHttpHandler CreateHandler(LiteHttpClientOptions options) => new()
    {
        PooledConnectionLifetime    = options.PooledConnectionLifetime,
        PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout,
        MaxConnectionsPerServer     = options.MaxConnectionsPerServer,
        EnableMultipleHttp2Connections = true,
        KeepAlivePingPolicy  = HttpKeepAlivePingPolicy.WithActiveRequests,
        KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
        KeepAlivePingDelay   = TimeSpan.FromSeconds(30),
        AutomaticDecompression = DecompressionMethods.GZip
                               | DecompressionMethods.Deflate
                               | DecompressionMethods.Brotli,
        UseCookies     = options.UseCookies,
        ConnectTimeout = options.ConnectTimeout,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Public surface — GET
    // ─────────────────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> GetAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, url, body: null, configure, ct);

    public async Task<T?> GetJsonAsync<T>(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
    {
        using var response = await GetAsync(url, configure, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, ct).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public surface — POST / PUT / PATCH / DELETE
    // ─────────────────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> PostAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, url, body: null, configure, ct);

    public Task<HttpResponseMessage> PostJsonAsync<T>(
        string url,
        T payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
        => SendJsonAsync(HttpMethod.Post, url, payload, configure, ct);

    public async Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
    {
        using var response = await PostJsonAsync(url, payload, configure, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await ReadJsonAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> PutJsonAsync<T>(
        string url,
        T payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
        => SendJsonAsync(HttpMethod.Put, url, payload, configure, ct);

    public async Task<TResponse?> PutJsonAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
    {
        using var response = await PutJsonAsync(url, payload, configure, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await ReadJsonAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> PatchJsonAsync<T>(
        string url,
        T payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
        => SendJsonAsync(HttpMethod.Patch, url, payload, configure, ct);

    public async Task<TResponse?> PatchJsonAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
    {
        using var response = await PatchJsonAsync(url, payload, configure, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await ReadJsonAsync<TResponse>(response, ct).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> DeleteAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, url, body: null, configure, ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Public surface — form / multipart
    // ─────────────────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> fields,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
    {
        // FormUrlEncodedContent buffers in memory so it is inherently replayable
        var fields_list = new List<KeyValuePair<string, string>>(fields);
        return SendCoreAsync(HttpMethod.Post, url, () => new FormUrlEncodedContent(fields_list), configure, ct);
    }

    public Task<HttpResponseMessage> PostMultipartAsync(
        string url,
        Action<MultipartFormDataContent> buildForm,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
    {
        return SendCoreAsync(HttpMethod.Post, url, () =>
        {
            var form = new MultipartFormDataContent();
            buildForm(form);
            return form;
        }, configure, ct);
    }

    public Task<HttpResponseMessage> PostMultipartAsync(
        string url,
        Func<MultipartContent> contentFactory,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default)
        => SendCoreAsync(HttpMethod.Post, url, () => contentFactory(), configure, ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Streaming — enumerate SSE / newline-delimited JSON with zero buffering
    // ─────────────────────────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> StreamLinesAsync(
        string url,
        Action<RequestOptions>? configure = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await GetAsync(url, o =>
        {
            // Run the caller's configure first, then force streaming on — otherwise a caller whose
            // configure delegate happens to touch ResponseHeadersRead (directly, or via a shared
            // helper) would silently disable streaming and force full-body buffering.
            configure?.Invoke(o);
            o.ResponseHeadersRead = true;
        }, ct).ConfigureAwait(false);

        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) yield break;
            if (line.Length > 0) yield return line;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core send path
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Low-level send with full control over the request message.
    /// When retries are enabled and a body is provided, the body is buffered to bytes
    /// so it can be replayed on each retry attempt.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        HttpContent? body,
        Action<RequestOptions>? configure,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var opts = new RequestOptions();
        configure?.Invoke(opts);
        int maxRetries = opts.MaxRetries ?? _options.DefaultMaxRetries;

        Func<HttpContent?>? bodyFactory = null;
        if (body is not null)
        {
            if (maxRetries > 0)
            {
                // Buffer once so each retry attempt gets a fresh, unread ByteArrayContent
                var bytes       = await body.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var contentType = body.Headers.ContentType?.ToString();
                bodyFactory = () =>
                {
                    var c = new ByteArrayContent(bytes);
                    if (contentType is not null)
                        c.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    return c;
                };
            }
            else
            {
                bodyFactory = () => body;
            }
        }

        return await SendCoreAsync(method, url, bodyFactory, opts, maxRetries, ct).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpMethod method,
        string url,
        T payload,
        Action<RequestOptions>? configure,
        CancellationToken ct)
    {
        // Serialize once; factory creates a fresh ByteArrayContent per retry
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _options.JsonSerializerOptions);
        return SendCoreAsync(method, url, () => new ByteArrayContent(bytes)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } },
        }, configure, ct);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string url,
        Func<HttpContent?>? bodyFactory,
        Action<RequestOptions>? configure,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var opts = new RequestOptions();
        configure?.Invoke(opts);
        int maxRetries = opts.MaxRetries ?? _options.DefaultMaxRetries;

        return await SendCoreAsync(method, url, bodyFactory, opts, maxRetries, ct).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string url,
        Func<HttpContent?>? bodyFactory,
        RequestOptions opts,
        int maxRetries,
        CancellationToken ct)
    {
        var timeout = opts.Timeout ?? _options.DefaultTimeout;
        return ExecuteWithRetryAsync(method, url, bodyFactory, opts, maxRetries, timeout, ct);
    }

    /// <summary>
    /// Each attempt gets its own <paramref name="timeout"/> window (linked to the caller's <paramref name="ct"/>).
    /// A per-attempt timeout is treated like any other transient failure and consumes one retry rather than
    /// aborting the whole operation, so a fixed timeout doesn't get eaten by an earlier slow attempt.
    /// </summary>
    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        HttpMethod method,
        string url,
        Func<HttpContent?>? bodyFactory,
        RequestOptions opts,
        int maxRetries,
        TimeSpan timeout,
        CancellationToken ct)
    {
        int attempts = 0;

        while (true)
        {
            using var request    = BuildRequest(method, url, bodyFactory?.Invoke(), opts);
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            _logger.LogInformation("Sending HTTP request {Method} {Url}", method, url);
            if (_logger.IsEnabled(LogLevel.Trace))
                LogHeaders("Request", request.Headers, request.Content?.Headers);

            HttpResponseMessage? response = null;
            var sw = Stopwatch.StartNew();

            try
            {
                var completion = opts.ResponseHeadersRead
                    ? HttpCompletionOption.ResponseHeadersRead
                    : HttpCompletionOption.ResponseContentRead;

                response = _resiliencePipeline is not null
                    ? await _resiliencePipeline.ExecuteAsync(
                        async token => await _inner.SendAsync(request, completion, token).ConfigureAwait(false),
                        linkedCts.Token).ConfigureAwait(false)
                    : await _inner.SendAsync(request, completion, linkedCts.Token).ConfigureAwait(false);

                if (ShouldRetry(response.StatusCode) && attempts < maxRetries)
                {
                    var retryAfter = GetRetryAfterDelay(response);
                    _logger.LogWarning(
                        "Retry {Attempt}/{Max} for {Method} {Url} — received {StatusCode}",
                        attempts + 1, maxRetries, method, url, (int)response.StatusCode);
                    response.Dispose();
                    response = null;
                    await DelayAsync(attempts, retryAfter, ct).ConfigureAwait(false);
                    attempts++;
                    continue;
                }

                _logger.LogInformation(
                    "Received HTTP response {StatusCode} for {Method} {Url} after {ElapsedMs}ms",
                    (int)response.StatusCode, method, url, sw.ElapsedMilliseconds);
                if (_logger.IsEnabled(LogLevel.Trace))
                    LogHeaders("Response", response.Headers, response.Content?.Headers);

                return response;
            }
            catch (OperationCanceledException oce)
                when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                response?.Dispose();

                if (attempts < maxRetries)
                {
                    _logger.LogWarning(
                        "Retry {Attempt}/{Max} for {Method} {Url} — attempt timed out after {Timeout}s",
                        attempts + 1, maxRetries, method, url, timeout.TotalSeconds);
                    await DelayAsync(attempts, retryAfter: null, ct).ConfigureAwait(false);
                    attempts++;
                    continue;
                }

                _logger.LogWarning("Request {Method} {Url} timed out after {Timeout}s",
                    method, url, timeout.TotalSeconds);
                throw new TimeoutException(
                    $"Request '{method} {url}' timed out after {timeout.TotalSeconds}s ({attempts + 1} attempt(s)).", oce);
            }
            catch (Exception ex) when (IsTransient(ex) && attempts < maxRetries)
            {
                response?.Dispose();
                _logger.LogWarning(ex,
                    "Retry {Attempt}/{Max} for {Method} {Url} — {ExceptionType}",
                    attempts + 1, maxRetries, method, url, ex.GetType().Name);
                await DelayAsync(attempts, retryAfter: null, ct).ConfigureAwait(false);
                attempts++;
            }
        }
    }

    /// <summary>Reads a server-provided Retry-After delay, capped at <see cref="LiteHttpClientOptions.MaxRetryAfterDelay"/>.</summary>
    private TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null) return null;

        TimeSpan? delay = retryAfter.Delta is { } delta
            ? delta
            : retryAfter.Date is { } date
                ? date - DateTimeOffset.UtcNow
                : null;

        if (delay is null || delay <= TimeSpan.Zero) return null;

        var cap = _options.MaxRetryAfterDelay;
        return delay > cap ? cap : delay;
    }

    private void LogHeaders(
        string label,
        System.Net.Http.Headers.HttpHeaders headers,
        System.Net.Http.Headers.HttpContentHeaders? contentHeaders)
    {
        foreach (var (name, values) in headers)
            _logger.LogTrace("  {Label} {Name}: {Value}", label, name, string.Join(", ", values));
        if (contentHeaders is null) return;
        foreach (var (name, values) in contentHeaders)
            _logger.LogTrace("  {Label} {Name}: {Value}", label, name, string.Join(", ", values));
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        string url,
        HttpContent? content,
        RequestOptions opts)
    {
        var msg = new HttpRequestMessage(method, url);

        if (content is not null)
            msg.Content = content;

        if (opts.Headers is { Count: > 0 })
            foreach (var (k, v) in opts.Headers)
                msg.Headers.TryAddWithoutValidation(k, v);

        if (opts.BasicCredentials is { } basic)
            msg.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.Username}:{basic.Password}")));

        if (opts.BearerToken is not null)
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.BearerToken);

        if (opts.ApiKey is not null)
            msg.Headers.TryAddWithoutValidation("X-Api-Key", opts.ApiKey);

        return msg;
    }

    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, _options.JsonSerializerOptions, ct).ConfigureAwait(false);
    }

    private static bool ShouldRetry(HttpStatusCode code) =>
        code is HttpStatusCode.RequestTimeout          // 408
             or HttpStatusCode.TooManyRequests         // 429
             or HttpStatusCode.InternalServerError     // 500
             or HttpStatusCode.BadGateway              // 502
             or HttpStatusCode.ServiceUnavailable      // 503
             or HttpStatusCode.GatewayTimeout;         // 504

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TimeoutException or IOException;

    /// <summary>
    /// Delay before the next retry attempt. When the server supplied a Retry-After delay, that value
    /// is honored as-is (already capped by <see cref="GetRetryAfterDelay"/>); otherwise falls back to
    /// exponential back-off with full jitter: delay = rand(0, min(cap, base * 2^attempt)).
    /// </summary>
    private static Task DelayAsync(int attempt, TimeSpan? retryAfter, CancellationToken ct)
    {
        if (retryAfter is { } ra)
            return Task.Delay(ra, ct);

        const int baseMs = 200;
        const int capMs  = 10_000;
        var ceiling = Math.Min(capMs, baseMs * (1 << attempt));
        var delay   = Random.Shared.Next(0, ceiling);
        return Task.Delay(delay, ct);
    }

    /// <summary>
    /// Throws <see cref="LiteHttpRequestException"/> (carrying the response body) when the status
    /// code is not a success. Reading the body is best-effort — a failure to read it must not mask
    /// the original status error.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // best-effort only
        }

        var message = $"Request failed with status code {(int)response.StatusCode} ({response.StatusCode}).";
        if (!string.IsNullOrEmpty(body))
            message += $" Response body: {Truncate(body, 2000)}";

        throw new LiteHttpRequestException(response.StatusCode, body, message);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...(truncated)");

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inner.Dispose();
    }
}
