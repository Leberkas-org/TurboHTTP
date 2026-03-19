using System.Text;
using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// Demonstrates converting an ActorSystem-backed StreamTest into a plain [Fact] unit test.
/// Each test here mirrors a test from <c>TurboHttp.StreamTests.Http10.Http10EncoderStageTests</c>
/// but calls <see cref="Http10Encoder.Encode"/> directly — no ActorSystem, no Materializer.
/// </summary>
public sealed class Http10EncoderStageConversionExampleTests
{
    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = new Memory<byte>(new byte[bufferSize]);
        var written = Http10Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    /// <summary>
    /// Mirrors <c>Http10EncoderStageTests.ST_10_ENC_001_RequestLine_Format</c>.
    /// Original uses Source.Single → Http10EncoderStage → Sink.Seq via Akka Materializer.
    /// This version calls Http10Encoder.Encode directly — same assertion, no ActorSystem.
    /// </summary>
    [Fact(DisplayName = "RFC1945-5.1-RL-001: Request-Line is METHOD SP path SP HTTP/1.0 CRLF (plain unit test)")]
    public void Should_FormatRequestLine_When_PlainEncoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html")
        {
            Version = new Version(1, 0)
        };

        var raw = Encode(request);

        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
    }

    /// <summary>
    /// Mirrors <c>Http10EncoderStageTests.ST_10_ENC_002_CustomHeader_Forwarded</c>.
    /// </summary>
    [Fact(DisplayName = "RFC1945-7.1-HD-001: Custom header is forwarded verbatim (plain unit test)")]
    public void Should_ForwardCustomHeader_When_PlainEncoding()
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
    /// Mirrors <c>Http10EncoderStageTests.ST_10_ENC_003_NoHostHeader</c>.
    /// </summary>
    [Fact(DisplayName = "RFC1945-D.1-HD-002: No Host header emitted (plain unit test)")]
    public void Should_OmitHostHeader_When_PlainEncoding()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = new Version(1, 0)
        };

        var raw = Encode(request);

        Assert.DoesNotContain("Host:", raw);
    }

    /// <summary>
    /// Mirrors <c>Http10EncoderStageTests.ST_10_ENC_005_PostBody_FollowsHeaders</c>.
    /// </summary>
    [Fact(DisplayName = "RFC1945-D.1-BD-001: POST body bytes follow headers after double-CRLF (plain unit test)")]
    public void Should_PlacePostBodyAfterHeaders_When_PlainEncoding()
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
