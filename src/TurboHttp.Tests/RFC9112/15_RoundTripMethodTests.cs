using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11RoundTripMethodTests
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

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 GET → 200 OK round-trip")]
    public async Task Should_Return200_When_GetRoundTrip()
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
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 POST JSON → 201 Created round-trip")]
    public void Should_Return201Created_When_PostJsonRoundTrip()
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

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 PUT → 204 No Content round-trip")]
    public void Should_Return204NoContent_When_PutRoundTrip()
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

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 DELETE → 200 OK round-trip")]
    public void Should_Return200_When_DeleteRoundTrip()
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

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 PATCH → 200 OK round-trip")]
    public async Task Should_Return200_When_PatchRoundTrip()
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
        Assert.Equal(responseBody, await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 HEAD → Content-Length but no body")]
    public void Should_ReturnContentLengthHeader_When_HeadRoundTrip()
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

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 OPTIONS → 200 with Allow header")]
    public void Should_ReturnAllowHeader_When_OptionsRoundTrip()
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

    [Fact(DisplayName = "RFC9112-3.1: HTTP/1.1 request URL with query string preserved")]
    public void Should_EncodeQueryString_When_RequestHasQueryStringRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/search?q=hello+world&page=1");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.Contains("GET /search?q=hello+world&page=1 HTTP/1.1", encoded);
    }
}
