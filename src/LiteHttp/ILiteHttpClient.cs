using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiteHttp;

public interface ILiteHttpClient
{
    /// <inheritdoc cref="LiteHttpClient.GetAsync"/>
    Task<HttpResponseMessage> GetAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.GetJsonAsync{T}"/>
    Task<T?> GetJsonAsync<T>(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.PostAsync"/>
    Task<HttpResponseMessage> PostAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.PostJsonAsync{T}"/>
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

    /// <inheritdoc cref="LiteHttpClient.PutAsync"/>
    Task<HttpResponseMessage> PutAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.PutJsonAsync{T}"/>
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

    /// <inheritdoc cref="LiteHttpClient.PatchAsync"/>
    Task<HttpResponseMessage> PatchAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.PatchJsonAsync{T}"/>
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

    /// <inheritdoc cref="LiteHttpClient.DeleteAsync"/>
    Task<HttpResponseMessage> DeleteAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.PostFormAsync"/>
    Task<HttpResponseMessage> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> fields,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.PostMultipartAsync(string, Action{MultipartFormDataContent}, Action{RequestOptions}?, CancellationToken)"/>
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

    /// <inheritdoc cref="LiteHttpClient.StreamLinesAsync"/>
    IAsyncEnumerable<string> StreamLinesAsync(
        string url,
        Action<RequestOptions>? configure = null,
        CancellationToken ct = default);

    /// <inheritdoc cref="LiteHttpClient.SendAsync"/>
    Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        HttpContent? body,
        Action<RequestOptions>? configure,
        CancellationToken ct = default);
}
