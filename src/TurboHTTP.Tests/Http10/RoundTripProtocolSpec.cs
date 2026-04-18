using System.Net;
using System.Text;
using Decoder = TurboHTTP.Protocol.Http10.Decoder;
using Encoder = TurboHTTP.Protocol.Http10.Encoder;

namespace TurboHTTP.Tests.Http10;

public sealed class Http10RoundTripProtocolSpec
{
    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        var arr = new byte[65536];
        Span<byte> buffer = arr;
        var written = Encoder.Encode(request, ref buffer);
        return (arr[..written], written);
    }

    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_encode_http10version()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.Contains("HTTP/1.0", raw);
        Assert.DoesNotContain("HTTP/1.1", raw);
        Assert.DoesNotContain("HTTP/2", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_format_request_line_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api");
        request.Content = new StringContent("test");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        var firstLine = raw.Split("\r\n")[0];

        Assert.Matches(@"^[A-Z]+ /\S* HTTP/1\.0$", firstLine);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_use_crlf_line_endings()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.DoesNotContain("\n\n", raw); // No standalone LF
        Assert.Contains("\r\n", raw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_decode_three_digit_status_code()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_reset_decoder_state()
    {
        var decoder = new Decoder();

        // First response
        var data1 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 5", "Hello");
        decoder.TryDecode(data1, out var response1);
        Assert.NotNull(response1);

        // Reset decoder
        decoder.Reset();

        // Second response should decode correctly
        var data2 = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");
        var result2 = decoder.TryDecode(data2, out var response2);

        Assert.True(result2);
        Assert.Equal(HttpStatusCode.NotFound, response2!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_maintain_independent_decoder_states()
    {
        var decoder1 = new Decoder();
        var decoder2 = new Decoder();

        var data1 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        var data2 = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");

        decoder1.TryDecode(data1, out var response1);
        decoder2.TryDecode(data2, out var response2);

        Assert.Equal(HttpStatusCode.OK, response1!.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response2!.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_preserve_custom_reason_phrase()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 Everything is fine", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("Everything is fine", response!.ReasonPhrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_handle_case_insensitive_headers()
    {
        var decoder = new Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "content-type: text/plain\r\nCONTENT-LENGTH: 0");

        decoder.TryDecode(data, out var response);

        Assert.NotNull(response);
        Assert.True(response.Content.Headers.Contains("Content-Type") ||
                    response.Content.Headers.Contains("content-type"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_produce_deterministic_encoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        request.Headers.Add("X-Custom", "value");

        var (buffer1, written1) = EncodeRequest(request);
        var (buffer2, written2) = EncodeRequest(request);

        Assert.Equal(written1, written2);
        var bytes1 = buffer1[..written1];
        var bytes2 = buffer2[..written2];
        Assert.Equal(bytes1, bytes2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-1")]
    public void Http10RoundTripProtocolSpec_should_include_content_length()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("request body data")
        };
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.Contains("Content-Length:", raw);
    }
}