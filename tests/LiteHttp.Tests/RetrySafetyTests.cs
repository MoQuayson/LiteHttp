using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using LiteHttp;
using Xunit;

namespace LiteHttp.Tests;

public sealed class RetrySafetyTests
{
    private static LiteHttpClient Client(Func<int, Task<HttpResponseMessage>> respond, out Func<int> calls)
    {
        int n = 0;
        calls = () => n;
        return new LiteHttpClient(
            new StubHandler((_, _) => respond(++n)),
            new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 2 });
    }

    private static Task<HttpResponseMessage> Status(HttpStatusCode c) => Task.FromResult(new HttpResponseMessage(c));

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Post_Retries_On_Safe_Status(HttpStatusCode code)
    {
        using var c = Client(n => Status(n < 2 ? code : HttpStatusCode.OK), out var calls);
        using var r = await c.PostAsync("/x");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(2, calls());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Post_Does_Not_Retry_Ambiguous_Status(HttpStatusCode code)
    {
        using var c = Client(_ => Status(code), out var calls);
        using var r = await c.PostAsync("/x");
        Assert.Equal(code, r.StatusCode);
        Assert.Equal(1, calls());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Get_And_Put_Still_Retry_Ambiguous_Status(HttpStatusCode code)
    {
        using var c = Client(n => Status(n < 2 ? code : HttpStatusCode.OK), out var calls);
        using var r = await c.GetAsync("/x");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(2, calls());

        using var c2 = Client(n => Status(n < 2 ? code : HttpStatusCode.OK), out var calls2);
        using var r2 = await c2.PutAsync("/x");
        Assert.Equal(2, calls2());
    }

    [Fact]
    public async Task Post_Opt_In_Retries_Ambiguous_Status()
    {
        using var c = Client(n => Status(n < 2 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK), out var calls);
        using var r = await c.PostAsync("/x", o => o.RetryNonIdempotent = true);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(2, calls());
    }

    [Fact]
    public async Task Post_Does_Not_Retry_Mid_Flight_IOException()
    {
        using var c = Client(_ => throw new IOException("reset"), out var calls);
        await Assert.ThrowsAsync<IOException>(() => c.PostAsync("/x"));
        Assert.Equal(1, calls());
    }

    [Fact]
    public async Task Post_Retries_Pre_Send_Connection_Error()
    {
        using var c = Client(n => n < 2
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
            : Status(HttpStatusCode.OK), out var calls);
        using var r = await c.PostAsync("/x");
        Assert.Equal(2, calls());
    }

    [Fact]
    public async Task Post_Does_Not_Retry_Other_HttpRequestError()
    {
        using var c = Client(_ => throw new HttpRequestException(HttpRequestError.ResponseEnded, "cut off"), out var calls);
        await Assert.ThrowsAsync<HttpRequestException>(() => c.PostAsync("/x"));
        Assert.Equal(1, calls());
    }

    [Fact]
    public async Task Post_Does_Not_Retry_Attempt_Timeout()
    {
        int n = 0;
        using var c = new LiteHttpClient(new StubHandler(async (_, ct) =>
        {
            n++;
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }), new LiteHttpClientOptions
        {
            BaseAddress = new Uri("https://test.example.com"),
            DefaultMaxRetries = 2,
            DefaultTimeout = TimeSpan.FromMilliseconds(100),
        });
        await Assert.ThrowsAsync<TimeoutException>(() => c.PostAsync("/x"));
        Assert.Equal(1, n);
    }
}
