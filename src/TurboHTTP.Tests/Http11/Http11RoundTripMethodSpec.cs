using System.Net;
using System.Text;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11;

/// <summary>
/// Tests round-trip encoding and decoding of HTTP methods per RFC 9112 §3.
/// Verifies that all standard methods survive a full encode → decode cycle.
/// </summary>
/// <remarks>
/// Classes under test: <see cref="Http11Encoder"/> and <see cref="Http11Decoder"/>.
/// RFC 9112 §3: Method token must be preserved verbatim through encode/decode.
/// </remarks>
public sealed class Http11RoundTripMethodSpec
{
    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        var buffer = new byte[65536];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        return (buffer, written);
    }

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
    [Trait("RFC", "RFC9112-3")]
    public async Task Http11RoundTrip_should_return_200_when_get_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.StartsWith("GET /api HTTP/1.1\r\n", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "hello", ("Content-Length", "5"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_201_created_when_post_json_round_trip()
    {
        const string json = "{\"name\":\"Alice\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/users")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("POST /users HTTP/1.1", encoded);
        Assert.Contains("Content-Type: application/json", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(201, "Created", "",
            ("Content-Length", "0"), ("Location", "/users/42"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Location", out var loc));
        Assert.Equal("/users/42", loc.Single());
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_204_no_content_when_put_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource/1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("PUT /resource/1 HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_200_when_delete_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/5");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("DELETE /resource/5 HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public async Task Http11RoundTrip_should_return_200_when_patch_round_trip()
    {
        const string patch = "{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"Bob\"}";
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), "http://example.com/item/3")
        {
            Content = new StringContent(patch, Encoding.UTF8, "application/json-patch+json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("PATCH /item/3 HTTP/1.1", encoded);

        const string responseBody = "{\"id\":3}";
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", responseBody,
            ("Content-Length", responseBody.Length.ToString()),
            ("Content-Type", "application/json"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(responseBody, await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_content_length_header_when_head_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.StartsWith("HEAD /resource HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Content-Type", "application/octet-stream"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_return_allow_header_when_options_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/resource");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("OPTIONS /resource HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Allow", "GET, POST, PUT, DELETE, OPTIONS"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.True(responses[0].Content.Headers.TryGetValues("Allow", out var allowVals));
        Assert.Contains("GET", string.Join(",", allowVals));
    }

    [Fact]
    [Trait("RFC", "RFC9112-3")]
    public void Http11RoundTrip_should_encode_query_string_when_request_has_query_string_round_trip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/search?q=hello+world&page=1");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.Contains("GET /search?q=hello+world&page=1 HTTP/1.1", encoded);
    }
}
