using System.Net;
using System.Net.Http;

namespace LiteHttp;

/// <summary>
/// Thrown by the typed JSON overloads (and <c>StreamLinesAsync</c>) when a response has a
/// non-success status code. Unlike the base <see cref="HttpRequestException"/> thrown by
/// <c>EnsureSuccessStatusCode</c>, this carries the response body so failures are diagnosable
/// without re-instrumenting the call site.
/// </summary>
public sealed class LiteHttpRequestException : HttpRequestException
{
    /// <summary>The response body, if it could be read. Not truncated — see <see cref="HttpRequestException.Message"/> for a truncated preview.</summary>
    public string? ResponseBody { get; }

    public LiteHttpRequestException(HttpStatusCode statusCode, string? responseBody, string message)
        : base(message, inner: null, statusCode: statusCode)
    {
        ResponseBody = responseBody;
    }
}
