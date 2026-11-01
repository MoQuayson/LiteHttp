using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteHttp;
using Xunit;

namespace LiteHttp.Tests;

/// <summary>
/// Exercises the real SocketsHttpHandler path against a loopback TLS server presenting a self-signed cert.
/// </summary>
public sealed class CertificateValidationTests
{
    private static X509Certificate2 CreateSelfSigned(string dnsName)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Round-trip through PFX so the private key is usable by SslStream on all platforms.
        return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
    }

    private static string Pin(X509Certificate2 c) => Convert.ToHexString(SHA256.HashData(c.RawData));

    private sealed class TlsServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public TlsServer(X509Certificate2 cert)
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient tcp;
                    try { tcp = await _listener.AcceptTcpClientAsync(_cts.Token); }
                    catch { return; }
                    _ = Task.Run(async () =>
                    {
                        using var _ = tcp;
                        try
                        {
                            using var ssl = new SslStream(tcp.GetStream());
                            await ssl.AuthenticateAsServerAsync(cert, false, false);
                            var buf = new byte[4096];
                            await ssl.ReadAsync(buf);
                            await ssl.WriteAsync(Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
                            await ssl.FlushAsync();
                        }
                        catch { /* client rejected the cert */ }
                    });
                }
            });
        }

        public void Dispose() { _cts.Cancel(); _listener.Stop(); }
    }

    private static LiteHttpClient Client(TlsServer s, LiteHttpClientOptions opts)
        => new(opts with { BaseAddress = new Uri($"https://localhost:{s.Port}"), DefaultMaxRetries = 0 });

    private static async Task<HttpStatusCode> GetStatus(LiteHttpClient c)
    {
        using var r = await c.GetAsync("/");
        return r.StatusCode;
    }

    [Fact]
    public async Task Default_Rejects_SelfSigned()
    {
        using var cert = CreateSelfSigned("localhost");
        using var server = new TlsServer(cert);
        using var client = Client(server, new LiteHttpClientOptions());
        await Assert.ThrowsAsync<HttpRequestException>(() => GetStatus(client));
    }

    [Fact]
    public async Task TrustedCa_Accepts_Cert()
    {
        using var cert = CreateSelfSigned("localhost");
        using var server = new TlsServer(cert);
        using var client = Client(server, new LiteHttpClientOptions
        {
            TrustedCaCertificates = new X509Certificate2Collection(new X509Certificate2(cert.RawData)),
        });
        Assert.Equal(HttpStatusCode.OK, await GetStatus(client));
    }

    [Fact]
    public async Task TrustedCa_Rejects_Unrelated_Cert()
    {
        using var cert = CreateSelfSigned("localhost");
        using var other = CreateSelfSigned("localhost");
        using var server = new TlsServer(cert);
        using var client = Client(server, new LiteHttpClientOptions
        {
            TrustedCaCertificates = new X509Certificate2Collection(new X509Certificate2(other.RawData)),
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => GetStatus(client));
    }

    [Fact]
    public async Task Pin_Accepts_Matching_Including_Colons_And_Lowercase()
    {
        using var cert = CreateSelfSigned("localhost");
        using var server = new TlsServer(cert);
        var hex = Pin(cert).ToLowerInvariant();
        var colons = string.Join(":", Enumerable.Range(0, 32).Select(i => hex.Substring(i * 2, 2)));
        using var client = Client(server, new LiteHttpClientOptions { PinnedCertificateSha256 = new[] { colons } });
        Assert.Equal(HttpStatusCode.OK, await GetStatus(client));
    }

    [Fact]
    public async Task Pin_Rejects_Wrong_Pin()
    {
        using var cert = CreateSelfSigned("localhost");
        using var server = new TlsServer(cert);
        using var client = Client(server, new LiteHttpClientOptions
        {
            PinnedCertificateSha256 = new[] { new string('0', 64) },
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => GetStatus(client));
    }

    [Fact]
    public async Task Pin_Does_Not_Bypass_Hostname_Mismatch()
    {
        using var cert = CreateSelfSigned("not-localhost.example");
        using var server = new TlsServer(cert);
        using var client = Client(server, new LiteHttpClientOptions { PinnedCertificateSha256 = new[] { Pin(cert) } });
        await Assert.ThrowsAsync<HttpRequestException>(() => GetStatus(client));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Callback_Decides_Outcome(bool accept)
    {
        using var cert = CreateSelfSigned("localhost");
        using var server = new TlsServer(cert);
        var called = false;
        using var client = Client(server, new LiteHttpClientOptions
        {
            ServerCertificateValidationCallback = (_, _, _, _) => { called = true; return accept; },
        });
        if (accept) Assert.Equal(HttpStatusCode.OK, await GetStatus(client));
        else await Assert.ThrowsAsync<HttpRequestException>(() => GetStatus(client));
        Assert.True(called);
    }

    [Fact]
    public void Malformed_Pin_Throws_At_Construction()
    {
        Assert.Throws<ArgumentException>(() => new LiteHttpClient(new LiteHttpClientOptions
        {
            PinnedCertificateSha256 = new[] { "not-a-pin" },
        }));
    }

    [Fact]
    public void No_Tls_Options_Means_No_Callback()
        => Assert.Null(CertificateValidation.Create(new LiteHttpClientOptions()));
}
