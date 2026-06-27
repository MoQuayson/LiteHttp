using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteHttp;
using Xunit;

namespace LiteHttp.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct) => send(request, ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LiteHttpClientTests
{
    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJsonAsync_Returns_Deserialised_Payload()
    {
        var expected = new { Id = 42, Name = "Moses" };
        var json     = JsonSerializer.Serialize(expected);

        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("/users/42", req.RequestUri!.PathAndQuery);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }), new LiteHttpClientOptions
        {
            BaseAddress       = new Uri("https://api.example.com"),
            DefaultMaxRetries = 0,
        });

        var result = await client.GetJsonAsync<System.Text.Json.Nodes.JsonNode>("/users/42");

        Assert.NotNull(result);
        Assert.Equal(42,      result!["Id"]!.GetValue<int>());
        Assert.Equal("Moses", result!["Name"]!.GetValue<string>());
    }

    // ── POST ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostJsonAsync_Sends_Correct_ContentType()
    {
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post,    req.Method);
            Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        using var response = await client.PostJsonAsync("/items", new { Name = "widget" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Typed response overload ───────────────────────────────────────────────

    [Fact]
    public async Task PostJsonTyped_DeserializesResponseBody()
    {
        var returned = new { Id = 99, Name = "created" };
        var json     = JsonSerializer.Serialize(returned);

        using var client = new LiteHttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            })), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        var result = await client.PostJsonAsync<object, System.Text.Json.Nodes.JsonNode>(
            "/items", new { Name = "widget" });

        Assert.NotNull(result);
        Assert.Equal(99,        result!["Id"]!.GetValue<int>());
        Assert.Equal("created", result!["Name"]!.GetValue<string>());
    }

    // ── DELETE ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Sends_DELETE_Verb()
    {
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        using var response = await client.DeleteAsync("/items/1");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Per-request headers ───────────────────────────────────────────────────

    [Fact]
    public async Task RequestOptions_Bearer_Sets_Authorization_Header()
    {
        const string token = "test-jwt-token";
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal(token,    req.Headers.Authorization!.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        await client.GetJsonAsync<object>("/secure", o => o.WithBearer(token));
    }

    [Fact]
    public async Task RequestOptions_Basic_Sets_Authorization_Header()
    {
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal("Basic", req.Headers.Authorization!.Scheme);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(req.Headers.Authorization!.Parameter!));
            Assert.Equal("alice:s3cr3t", decoded);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        await client.GetJsonAsync<object>("/secure", o => o.WithBasic("alice", "s3cr3t"));
    }

    [Fact]
    public async Task RequestOptions_ApiKey_Sets_XApiKey_Header()
    {
        const string key = "super-secret";
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(key, req.Headers.GetValues("X-Api-Key").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        await client.GetJsonAsync<object>("/secret", o => o.WithApiKey(key));
    }

    [Fact]
    public async Task RequestOptions_CustomHeader_Is_Sent()
    {
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal("trace-123", req.Headers.GetValues("X-Request-ID").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        await client.GetJsonAsync<object>("/trace", o => o.WithHeader("X-Request-ID", "trace-123"));
    }

    // ── Retry logic ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Retries_On_503_Up_To_MaxRetries()
    {
        int callCount = 0;
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            callCount++;
            var status = callCount < 3
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 3 });

        using var response = await client.GetAsync("/flaky");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task Returns_Last_Failure_When_Retries_Exhausted()
    {
        using var client = new LiteHttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 2 });

        using var response = await client.GetAsync("/always-fails");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task PostJson_RetryPreservesRequestBody()
    {
        var bodies    = new List<string>();
        int callCount = 0;

        using var client = new LiteHttpClient(new StubHandler(async (req, _) =>
        {
            callCount++;
            bodies.Add(await req.Content!.ReadAsStringAsync());
            var status = callCount < 2
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return new HttpResponseMessage(status);
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 2 });

        using var _ = await client.PostJsonAsync("/endpoint", new { Value = 42 });

        Assert.Equal(2, callCount);
        Assert.All(bodies, b => Assert.Contains("42", b));
    }

    // ── Timeout disambiguation ────────────────────────────────────────────────

    [Fact]
    public async Task Timeout_Throws_TimeoutException_Not_OperationCanceledException()
    {
        using var client = new LiteHttpClient(new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }), new LiteHttpClientOptions
        {
            BaseAddress       = new Uri("https://test.example.com"),
            DefaultTimeout    = TimeSpan.FromMilliseconds(50),
            DefaultMaxRetries = 0,
        });

        await Assert.ThrowsAsync<TimeoutException>(() => client.GetAsync("/slow"));
    }

    // ── Stream lines ──────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamLinesAsync_Yields_Non_Empty_Lines()
    {
        const string body = "line one\n\nline two\nline three\n";
        using var client = new LiteHttpClient(new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body))),
            };
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        var lines = new List<string>();
        await foreach (var line in client.StreamLinesAsync("/sse"))
            lines.Add(line);

        Assert.Equal(["line one", "line two", "line three"], lines);
    }

    // ── Multipart (form-data) ─────────────────────────────────────────────────

    [Fact]
    public async Task PostMultipartAsync_FormData_Sends_MultipartFormData_ContentType()
    {
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal("multipart/form-data", req.Content!.Headers.ContentType!.MediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        using var response = await client.PostMultipartAsync("/upload", form =>
            form.Add(new StringContent("hello"), "field"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Multipart (raw factory overload) ──────────────────────────────────────

    [Fact]
    public async Task PostMultipartAsync_Factory_Sends_Correct_Subtype()
    {
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal("multipart/mixed", req.Content!.Headers.ContentType!.MediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        using var response = await client.PostMultipartAsync("/batch", () =>
        {
            var content = new MultipartContent("mixed");
            content.Add(new StringContent("part one"));
            return content;
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostMultipartAsync_Factory_Rebuilds_Content_On_Each_Retry()
    {
        int factoryCallCount = 0;
        int handlerCallCount = 0;

        using var client = new LiteHttpClient(new StubHandler((_, _) =>
        {
            handlerCallCount++;
            var status = handlerCallCount < 3
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 3 });

        using var response = await client.PostMultipartAsync("/retry-target", () =>
        {
            factoryCallCount++;
            var content = new MultipartContent("mixed");
            content.Add(new StringContent($"attempt {factoryCallCount}"));
            return content;
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, factoryCallCount);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_ObjectDisposedException_After_Dispose()
    {
        var client = new LiteHttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
            new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.GetAsync("/any"));
    }

    // ── Form POST ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostFormAsync_Sends_FormUrlEncoded_ContentType()
    {
        using var client = new LiteHttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal("application/x-www-form-urlencoded",
                req.Content!.Headers.ContentType!.MediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }), new LiteHttpClientOptions { BaseAddress = new Uri("https://test.example.com"), DefaultMaxRetries = 0 });

        var fields = new[] { new KeyValuePair<string, string>("key", "value") };
        using var response = await client.PostFormAsync("/form", fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
