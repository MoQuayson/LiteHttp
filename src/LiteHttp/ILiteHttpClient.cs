using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiteHttp;

public interface ILiteHttpClient
{
    Task<HttpResponseMessage> GetAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<T?> GetJsonAsync<T>(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> PostAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> PostJsonAsync<T>(
        string url,
        T payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <summary>Posts JSON and deserialises the response body into <typeparamref name="TResponse"/>.</summary>
    Task<TResponse?> PostJsonAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> PutJsonAsync<T>(
        string url,
        T payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <summary>Puts JSON and deserialises the response body into <typeparamref name="TResponse"/>.</summary>
    Task<TResponse?> PutJsonAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> PatchJsonAsync<T>(
        string url,
        T payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <summary>Patches JSON and deserialises the response body into <typeparamref name="TResponse"/>.</summary>
    Task<TResponse?> PatchJsonAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> DeleteAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> fields,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> PostMultipartAsync(
        string url,
        Action<MultipartFormDataContent> buildForm,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <summary>
    /// Posts any <see cref="MultipartContent"/> subtype (form-data, mixed, related, …).
    /// The factory is invoked on each retry attempt to produce a fresh, unread instance.
    /// </summary>
    Task<HttpResponseMessage> PostMultipartAsync(
        string url,
        Func<MultipartContent> contentFactory,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamLinesAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        HttpContent? body,
        Action<RequestOptions>? configure,
        CancellationToken ct = default);
}
