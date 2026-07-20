using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace LiteHttp.Extensions;

/// <summary>
/// Extension methods for registering <see cref="LiteHttpClient"/> in an
/// <see cref="IServiceCollection"/> with proper singleton scoping.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="LiteHttpClient"/> with default options.
    /// </summary>
    public static IServiceCollection AddLiteHttpClient(this IServiceCollection services)
        => services.AddLiteHttpClient(new LiteHttpClientOptions());

    /// <summary>
    /// Registers a singleton <see cref="LiteHttpClient"/> from a pre-built
    /// <see cref="LiteHttpClientOptions"/> record.
    /// </summary>
    public static IServiceCollection AddLiteHttpClient(
        this IServiceCollection services,
        LiteHttpClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(sp => new LiteHttpClient(
            options,
            sp.GetService<ILogger<LiteHttpClient>>()));

        return services;
    }

    /// <summary>
    /// Registers a named singleton — useful when multiple remote endpoints are used.
    /// Retrieve via <see cref="ILiteHttpClientFactory"/>.
    /// </summary>
    public static IServiceCollection AddNamedLiteHttpClient(
        this IServiceCollection services,
        string name,
        LiteHttpClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton<ILiteHttpClientFactory, DefaultLiteHttpClientFactory>();
        services.AddSingleton(sp => new NamedClient(name, new LiteHttpClient(
            options,
            sp.GetService<ILogger<LiteHttpClient>>())));

        return services;
    }

    /// <summary>
    /// Registers a named singleton <see cref="LiteHttpClient"/> backed by a
    /// <see cref="ResiliencePipeline{T}"/>. Retrieve via <see cref="ILiteHttpClientFactory"/>.
    /// The client's own retry is disabled (<c>DefaultMaxRetries = 0</c>) to avoid double-retry.
    /// </summary>
    /// <remarks>
    /// The client's per-attempt <c>DefaultTimeout</c> (or a per-request <c>RequestOptions.Timeout</c>)
    /// bounds the *entire* resilience pipeline execution, including every retry the pipeline performs
    /// internally — it is not a fresh budget per Polly attempt. Size the timeout to comfortably cover
    /// your worst-case pipeline run (e.g. max retries × max per-try delay × expected upstream latency),
    /// or a multi-retry Polly policy can be cut off mid-pipeline by the outer timeout.
    /// </remarks>
    public static IServiceCollection AddNamedLiteHttpClientWithResilience(
        this IServiceCollection services,
        string name,
        LiteHttpClientOptions options,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);

        var pipeline = BuildPipeline(configureResilience);

        services.TryAddSingleton<ILiteHttpClientFactory, DefaultLiteHttpClientFactory>();
        services.AddSingleton(sp => new NamedClient(name, new LiteHttpClient(
            options with { DefaultMaxRetries = 0 },
            sp.GetService<ILogger<LiteHttpClient>>(),
            pipeline)));

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="LiteHttpClient"/> backed by a
    /// <see cref="ResiliencePipeline{T}"/> for retry, circuit-breaker, and timeout.
    /// The client's own retry is disabled (<c>DefaultMaxRetries = 0</c>) to avoid double-retry.
    /// </summary>
    /// <remarks>
    /// The client's per-attempt <c>DefaultTimeout</c> (or a per-request <c>RequestOptions.Timeout</c>)
    /// bounds the *entire* resilience pipeline execution, including every retry the pipeline performs
    /// internally — it is not a fresh budget per Polly attempt. Size the timeout to comfortably cover
    /// your worst-case pipeline run (e.g. max retries × max per-try delay × expected upstream latency),
    /// or a multi-retry Polly policy can be cut off mid-pipeline by the outer timeout.
    /// </remarks>
    public static IServiceCollection AddLiteHttpClientWithResilience(
        this IServiceCollection services,
        LiteHttpClientOptions options,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var pipeline = BuildPipeline(configureResilience);

        services.TryAddSingleton(sp => new LiteHttpClient(
            options with { DefaultMaxRetries = 0 },
            sp.GetService<ILogger<LiteHttpClient>>(),
            pipeline));

        return services;
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(
        Action<ResiliencePipelineBuilder<HttpResponseMessage>>? configure)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        if (configure is not null)
        {
            configure(builder);
        }
        else
        {
            builder
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 3,
                    BackoffType      = DelayBackoffType.Exponential,
                    UseJitter        = true,
                    ShouldHandle     = args => ValueTask.FromResult(
                        args.Outcome.Result?.StatusCode is
                            System.Net.HttpStatusCode.RequestTimeout or
                            System.Net.HttpStatusCode.TooManyRequests or
                            System.Net.HttpStatusCode.InternalServerError or
                            System.Net.HttpStatusCode.BadGateway or
                            System.Net.HttpStatusCode.ServiceUnavailable or
                            System.Net.HttpStatusCode.GatewayTimeout ||
                        args.Outcome.Exception is HttpRequestException or IOException),
                })
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    FailureRatio      = 0.5,
                    MinimumThroughput = 10,
                    SamplingDuration  = TimeSpan.FromSeconds(30),
                    BreakDuration     = TimeSpan.FromSeconds(15),
                });
        }

        return builder.Build();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Named client factory support
// ─────────────────────────────────────────────────────────────────────────────

public interface ILiteHttpClientFactory
{
    LiteHttpClient GetClient(string name);
}

internal sealed record NamedClient(string Name, LiteHttpClient Client);

internal sealed class DefaultLiteHttpClientFactory : ILiteHttpClientFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, LiteHttpClient> _map = new();
    private bool _disposed;

    public DefaultLiteHttpClientFactory(IEnumerable<NamedClient> clients)
    {
        foreach (var nc in clients)
        {
            if (!_map.TryAdd(nc.Name, nc.Client))
                throw new InvalidOperationException(
                    $"A LiteHttpClient named '{nc.Name}' is already registered. " +
                    "Named clients must be registered with unique names.");
        }
    }

    public LiteHttpClient GetClient(string name)
        => _map.TryGetValue(name, out var c)
               ? c
               : throw new InvalidOperationException($"No LiteHttpClient registered with name '{name}'.");

    /// <summary>
    /// Disposes every named client. The DI container only tracks and disposes the types it directly
    /// resolves — the <see cref="NamedClient"/> record wrapping each client is not itself disposable,
    /// so this factory is the only thing that owns their lifetime and must dispose them explicitly.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var client in _map.Values)
            client.Dispose();
    }
}
