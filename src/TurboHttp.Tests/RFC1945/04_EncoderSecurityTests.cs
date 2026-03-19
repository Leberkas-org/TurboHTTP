using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Tests.RFC1945;

public sealed class Http10EncoderSecurityTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    [Fact(DisplayName = "RFC1945-12-SC-001: CR in header value throws ArgumentException")]
    public void Should_ThrowArgumentException_When_HeaderValueContainsCr()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\rX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "RFC1945-12-SC-002: LF in header value throws ArgumentException")]
    public void Should_ThrowArgumentException_When_HeaderValueContainsLf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\nX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "RFC1945-12-SC-003: CRLF in header value throws ArgumentException")]
    public void Should_ThrowArgumentException_When_HeaderValueContainsCrLf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\r\nX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "RFC1945-12-SC-004: Header injection exception contains header name")]
    public void Should_IncludeHeaderNameInException_When_InjectionDetected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Dangerous", "bad\r\nvalue");

        var buffer = MakeBuffer();

        var ex = Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));

        Assert.Contains("X-Dangerous", ex.Message);
    }

    [Fact(DisplayName = "RFC1945-12-SC-005: Normal header value does not throw")]
    public void Should_NotThrow_When_HeaderValueIsNormal()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Safe", "perfectly-normal-value-123");

        var buffer = MakeBuffer();
        var ex = Record.Exception(() => Http10Encoder.Encode(request, ref buffer));

        Assert.Null(ex);
    }

    [Fact(DisplayName = "RFC1945-12-SC-006: Buffer too small for headers throws")]
    public void Should_ThrowInvalidOperationException_When_BufferTooSmallForHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(5);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "RFC1945-12-SC-007: Buffer too small for body throws")]
    public void Should_ThrowInvalidOperationException_When_BufferTooSmallForBody()
    {
        var largeBody = new byte[1000];
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var buffer = MakeBuffer(100);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact(DisplayName = "RFC1945-12-SC-008: Exact size buffer does not throw")]
    public void Should_NotThrow_When_BufferIsExactSize()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var measureBuffer = MakeBuffer();
        var needed = Http10Encoder.Encode(request, ref measureBuffer);

        var exactBuffer = MakeBuffer(needed);
        var ex = Record.Exception(() => Http10Encoder.Encode(request, ref exactBuffer));

        Assert.Null(ex);
    }

    [Fact(DisplayName = "RFC1945-12-SC-009: Empty buffer throws")]
    public void Should_ThrowInvalidOperationException_When_BufferIsEmpty()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(0);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }
}
