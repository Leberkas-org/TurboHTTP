using System.Text;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Demonstrates converting an ActorSystem-backed StreamTest into a plain [Fact] unit test.
/// Each test here mirrors a test from <c>TurboHTTP.StreamTests.Http10.Http10EncoderSpec</c>
/// but calls <see cref="Http10Encoder.Encode"/> directly — no ActorSystem, no Materializer.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Encoder"/>.
/// RFC 1945: HTTP/1.0 request encoding — StreamTest-to-unit-test conversion pattern.
/// </remarks>
public sealed class Http10EncoderConversionExampleSpec
{
    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        Span<byte> buffer = new byte[bufferSize];
        var written = Http10Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    /// <summary>
    /// Mirrors <c>Http10EncoderSpec.Http10Encoder_should_format_request_line</c>.
    /// Original uses Source.Single → TestHttp10Encoder → Sink.Seq via Akka Materializer.
    /// This version calls Http10Encoder.Encode directly — same assertion, no ActorSystem.
    /// </summary>
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10EncoderConversionExample_should_format_request_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = new Version(1, 0)
        };

        var raw = Encode(request);

        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
    }

    /// <summary>
    /// Mirrors <c>Http10EncoderSpec.Http10Encoder_should_forward_custom_header</c>.
    /// </summary>
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10EncoderConversionExample_should_forward_custom_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = new Version(1, 0)
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var raw = Encode(request);

        Assert.Contains("X-Custom: value\r\n", raw);
    }

    /// <summary>
    /// Mirrors <c>Http10EncoderSpec.Http10Encoder_should_omit_host_header</c>.
    /// </summary>
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10EncoderConversionExample_should_omit_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = new Version(1, 0)
        };

        var raw = Encode(request);

        Assert.DoesNotContain("Host:", raw);
    }

    /// <summary>
    /// Mirrors <c>Http10EncoderSpec.Http10Encoder_should_place_post_body_after_headers</c>.
    /// </summary>
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Http10EncoderConversionExample_should_place_post_body_after_headers()
    {
        var body = "hello"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = new Version(1, 0),
            Content = new ByteArrayContent(body)
        };

        var raw = Encode(request);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIndex >= 0, "Missing double-CRLF header/body separator");
        var bodyPart = raw[(separatorIndex + 4)..];
        Assert.Contains("hello", bodyPart);
    }
}
