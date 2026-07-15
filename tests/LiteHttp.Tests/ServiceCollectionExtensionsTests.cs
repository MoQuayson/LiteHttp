using System;
using System.Threading.Tasks;
using LiteHttp;
using LiteHttp.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LiteHttp.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task Disposing_The_Container_Disposes_Named_Clients()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<LiteHttpClient>>(NullLogger<LiteHttpClient>.Instance);
        services.AddNamedLiteHttpClient("test", new LiteHttpClientOptions
        {
            BaseAddress = new Uri("https://example.com"),
        });

        var provider = services.BuildServiceProvider();
        var factory  = provider.GetRequiredService<ILiteHttpClientFactory>();
        var client   = factory.GetClient("test");

        // Sanity check: the client is usable before the container is disposed.
        Assert.NotNull(client);

        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("/any"));
    }

    [Fact]
    public async Task Disposing_The_Container_Disposes_Multiple_Named_Clients_Independently()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<LiteHttpClient>>(NullLogger<LiteHttpClient>.Instance);
        services.AddNamedLiteHttpClient("a", new LiteHttpClientOptions { BaseAddress = new Uri("https://a.example.com") });
        services.AddNamedLiteHttpClient("b", new LiteHttpClientOptions { BaseAddress = new Uri("https://b.example.com") });

        var provider = services.BuildServiceProvider();
        var factory  = provider.GetRequiredService<ILiteHttpClientFactory>();
        var clientA  = factory.GetClient("a");
        var clientB  = factory.GetClient("b");

        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => clientA.GetAsync("/any"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => clientB.GetAsync("/any"));
    }

    [Fact]
    public void GetClient_Throws_For_Unregistered_Name()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<LiteHttpClient>>(NullLogger<LiteHttpClient>.Instance);
        services.AddNamedLiteHttpClient("known", new LiteHttpClientOptions { BaseAddress = new Uri("https://example.com") });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ILiteHttpClientFactory>();

        Assert.Throws<InvalidOperationException>(() => factory.GetClient("unknown"));
    }
}
