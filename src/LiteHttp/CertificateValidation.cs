using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LiteHttp;

/// <summary>Builds the opt-in server certificate validation callback from <see cref="LiteHttpClientOptions"/>.</summary>
internal static class CertificateValidation
{
    /// <returns>A callback, or <c>null</c> when no custom validation is configured (OS defaults apply).</returns>
    public static RemoteCertificateValidationCallback? Create(LiteHttpClientOptions options)
    {
        if (options.ServerCertificateValidationCallback is { } custom)
            return custom;

        var pins = ParsePins(options.PinnedCertificateSha256);
        var roots = options.TrustedCaCertificates is { Count: > 0 } c
            ? new X509Certificate2Collection(c)   // snapshot: later mutation by the caller has no effect
            : null;

        if (pins is null && roots is null)
            return null;

        return (_, certificate, chain, errors) =>
        {
            if (errors == SslPolicyErrors.None && roots is null)
                return true; // OS trust already succeeded and no custom roots restrict it

            if (certificate is null)
                return false;

            // Host name must always match; neither custom roots nor pins bypass it.
            if ((errors & (SslPolicyErrors.RemoteCertificateNameMismatch
                           | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
                return false;

            if (pins is not null && MatchesPin(certificate, pins))
                return true;

            if (roots is not null)
                return ChainTrustedByCustomRoots(certificate, chain, roots);

            return false;
        };
    }

    private static bool MatchesPin(X509Certificate certificate, List<byte[]> pins)
    {
        var hash = SHA256.HashData(certificate.GetRawCertData());
        foreach (var pin in pins)
            if (CryptographicOperations.FixedTimeEquals(hash, pin))
                return true;
        return false;
    }

    private static bool ChainTrustedByCustomRoots(
        X509Certificate certificate, X509Chain? presented, X509Certificate2Collection roots)
    {
        using var leaf = new X509Certificate2(certificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        // Intermediates the server sent along with the leaf.
        if (presented is not null)
            foreach (var element in presented.ChainElements)
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(leaf);
    }

    private static List<byte[]>? ParsePins(IReadOnlyCollection<string>? pins)
    {
        if (pins is not { Count: > 0 })
            return null;

        var result = new List<byte[]>(pins.Count);
        foreach (var pin in pins)
        {
            var hex = (pin ?? string.Empty).Replace(":", "").Replace(" ", "").Trim();
            if (hex.Length != 64)
                throw new ArgumentException(
                    $"Invalid {nameof(LiteHttpClientOptions.PinnedCertificateSha256)} entry: expected 64 hex characters (SHA-256).",
                    nameof(pins));
            try { result.Add(Convert.FromHexString(hex)); }
            catch (FormatException)
            {
                throw new ArgumentException(
                    $"Invalid {nameof(LiteHttpClientOptions.PinnedCertificateSha256)} entry: not valid hex.",
                    nameof(pins));
            }
        }
        return result;
    }
}
