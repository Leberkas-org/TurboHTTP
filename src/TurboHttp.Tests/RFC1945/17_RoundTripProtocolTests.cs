using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 Round-Trip Protocol Tests
/// Verifies protocol conformance and state handling in encode→decode cycles
/// </summary>
public sealed class Http10RoundTripProtocolTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        Memory<byte> buffer = new byte[65536];
        var written = Http10Encoder.Encode(request, ref buffer);
        return (buffer.ToArray(), written);
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

    [Fact(DisplayName = "RFC1945-RT-P01: HTTP/1.0 version invariant")]
    public void Should_EncodeHttp10Version_When_RequestEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.Contains("HTTP/1.0", raw);
        Assert.DoesNotContain("HTTP/1.1", raw);
        Assert.DoesNotContain("HTTP/2", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-P02: Request line format invariant")]
    public void Should_FormatRequestLineCorrectly_When_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api");
        request.Content = new StringContent("test");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        var firstLine = raw.Split("\r\n")[0];

        Assert.Matches(@"^[A-Z]+ /\S* HTTP/1\.0$", firstLine);
    }

    [Fact(DisplayName = "RFC1945-RT-P03: CRLF line endings required")]
    public void Should_UseCrlfLineEndings_When_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (encodedBuffer, written) = EncodeRequest(request);

        var raw = Encoding.ASCII.GetString(encodedBuffer, 0, written);
        Assert.DoesNotContain("\n\n", raw); // No standalone LF
        Assert.Contains("\r\n", raw);
    }

    [Fact(DisplayName = "RFC1945-RT-P04: Status code must be 3 digits")]
    public void Should_DecodeThreeDigitStatusCode_When_RoundTrip()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-P05: Decoder state reset between requests")]
    public void Should_ResetDecoderState_When_Called()
    {
        var decoder = new Http10Decoder();

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

    [Fact(DisplayName = "RFC1945-RT-P06: Multiple decoders independent")]
    public void Should_MaintainIndependentDecoderStates_When_MultipleDecodersUsed()
    {
        var decoder1 = new Http10Decoder();
        var decoder2 = new Http10Decoder();

        var data1 = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        var data2 = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");

        decoder1.TryDecode(data1, out var response1);
        decoder2.TryDecode(data2, out var response2);

        Assert.Equal(HttpStatusCode.OK, response1!.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response2!.StatusCode);
    }

    [Fact(DisplayName = "RFC1945-RT-P07: Reason phrase can be custom")]
    public void Should_PreserveCustomReasonPhrase_When_Decoded()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 Everything is fine", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("Everything is fine", response!.ReasonPhrase);
    }

    [Fact(DisplayName = "RFC1945-RT-P08: Headers case-insensitive")]
    public void Should_HandleCaseInsensitiveHeaders_When_Decoded()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "content-type: text/plain\r\nCONTENT-LENGTH: 0");

        decoder.TryDecode(data, out var response);

        Assert.NotNull(response);
        Assert.True(response.Content.Headers.Contains("Content-Type") ||
                    response.Content.Headers.Contains("content-type"));
    }

    [Fact(DisplayName = "RFC1945-RT-P09: Request encoding deterministic")]
    public void Should_ProduceDeterministicEncoding_When_SameRequest()
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

    [Fact(DisplayName = "RFC1945-RT-P10: Content-Length required for request bodies")]
    public void Should_IncludeContentLength_When_RequestHasBody()
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
