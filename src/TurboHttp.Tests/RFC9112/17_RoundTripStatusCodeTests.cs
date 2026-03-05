using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11RoundTripStatusCodeTests
{
    private static ReadOnlyMemory<byte> BuildResponse(int status, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact(DisplayName = "RFC7231-6.3: HTTP/1.1 GET → 301 with Location round-trip")]
    public void Should_Return301WithLocation_When_GetRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(301, "Moved Permanently", "",
            ("Content-Length", "0"),
            ("Location", "http://example.com/new-path"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.MovedPermanently, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Location", out var loc));
        Assert.Contains("new-path", loc.Single());
    }

    [Fact(DisplayName = "RFC7231-6.5: HTTP/1.1 GET → 404 Not Found round-trip")]
    public async Task Should_Return404_When_ResourceMissingRoundTrip()
    {
        const string body = "Not Found";
        var decoder = new Http11Decoder();
        var raw = BuildResponse(404, "Not Found", body, ("Content-Length", body.Length.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NotFound, responses[0].StatusCode);
        Assert.Equal("Not Found", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC7231-6.6: HTTP/1.1 GET → 500 Internal Server Error round-trip")]
    public void Should_Return500_When_ServerErrorRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(500, "Internal Server Error", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.InternalServerError, responses[0].StatusCode);
    }

    [Fact(DisplayName = "RFC7231-6.6: HTTP/1.1 503 Service Unavailable with Retry-After")]
    public void Should_Return503WithRetryAfter_When_ServiceUnavailableRoundTrip()
    {
        var decoder = new Http11Decoder();
        var raw = BuildResponse(503, "Service Unavailable", "",
            ("Content-Length", "0"),
            ("Retry-After", "120"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.Equal("120", retryAfter.Single());
    }
}
