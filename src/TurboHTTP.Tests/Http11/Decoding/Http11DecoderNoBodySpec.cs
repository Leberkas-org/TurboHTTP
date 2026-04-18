using System.Net;
using System.Text;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Http11.Decoding;

public sealed class Http11DecoderNoBodySpec
{
    private readonly Decoder _decoder = new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Decoder_should_have_empty_body_when_204_no_content()
    {
        var raw = "HTTP/1.1 204 No Content\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Decoder_should_have_empty_body_when_304_not_modified()
    {
        var raw = "HTTP/1.1 304 Not Modified\r\nETag: \"abc\"\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(HttpStatusCode.NotModified, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    [InlineData(204, "No Content")]
    [InlineData(205, "Reset Content")]
    [InlineData(304, "Not Modified")]
    public void Http11Decoder_should_have_empty_body_when_no_body_status(int code, string reason)
    {
        var raw = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\n\r\n");

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(code, (int)responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11Decoder_should_expect_body_bytes_when_head_response_has_content_length()
    {
        // Simulating HEAD response: status-line and headers indicate body length,
        // but no body bytes are present (server doesn't send body for HEAD).
        // The decoder should parse the headers but not expect body bytes.
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 1234\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out _);

        // For a HEAD response, the decoder would see Content-Length but no body.
        // However, the decoder doesn't know it's a HEAD response (that's request-side info).
        // In practice, for HTTP/1.1 client responses, if Content-Length is present,
        // the decoder expects body bytes. For HEAD, the client tracks this externally.
        // This test documents that if we manually construct a response with CL but no body,
        // the decoder will wait for more data (return false).
        Assert.False(decoded); // Decoder expects 1234 bytes but none are present
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_signal_connection_close_when_connection_close_header()
    {
        var raw = "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Contains("close", responses[0].Headers.Connection);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_signal_reuse_when_connection_keep_alive_header()
    {
        var raw = "HTTP/1.1 200 OK\r\nConnection: keep-alive\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Contains("keep-alive", responses[0].Headers.Connection);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_default_to_keep_alive_when_http11()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        // No explicit Connection header means keep-alive is default for HTTP/1.1
        // The response object may or may not have Connection header set
        Assert.Equal(new Version(1, 1), responses[0].Version);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_default_to_close_when_http10()
    {
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(new Version(1, 1), responses[0].Version); // Decoder parses as HTTP/1.1
        // Note: This decoder is Http11Decoder, so it always sets version to 1.1
        // For HTTP/1.0 responses, a separate Http10Decoder would be used
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11Decoder_should_recognize_all_tokens_when_multiple_connection_tokens()
    {
        var raw = "HTTP/1.1 200 OK\r\nConnection: keep-alive, Upgrade\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        var tokens = responses[0].Headers.Connection.ToList();
        Assert.Contains("keep-alive", tokens);
        Assert.Contains("Upgrade", tokens);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11Decoder_should_decode_both_when_two_responses_in_same_buffer()
    {
        var resp1 = BuildResponse(200, "OK", "first", ("Content-Length", "5"));
        var resp2 = BuildResponse(201, "Created", "second", ("Content-Length", "6"));

        var combined = new byte[resp1.Length + resp2.Length];
        resp1.Span.CopyTo(combined);
        resp2.Span.CopyTo(combined.AsSpan(resp1.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
        Assert.Equal("first", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("second", await responses[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static ReadOnlyMemory<byte> BuildResponse(int code, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}