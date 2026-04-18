using System.Net;
using System.Text;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11RoundTripStatusCodeSpec
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11RoundTrip_should_return_301_with_location_when_get_round_trip()
    {
        var decoder = new Decoder();
        var raw = BuildResponse(301, "Moved Permanently", "",
            ("Content-Length", "0"),
            ("Location", "http://example.com/new-path"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.MovedPermanently, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Location", out var loc));
        Assert.Contains("new-path", loc.Single());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public async Task Http11RoundTrip_should_return_404_when_resource_missing_round_trip()
    {
        const string body = "Not Found";
        var decoder = new Decoder();
        var raw = BuildResponse(404, "Not Found", body, ("Content-Length", body.Length.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NotFound, responses[0].StatusCode);
        Assert.Equal("Not Found", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11RoundTrip_should_return_500_when_server_error_round_trip()
    {
        var decoder = new Decoder();
        var raw = BuildResponse(500, "Internal Server Error", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.InternalServerError, responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11RoundTrip_should_return_503_with_retry_after_when_service_unavailable_round_trip()
    {
        var decoder = new Decoder();
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
