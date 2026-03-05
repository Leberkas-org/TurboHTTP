using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC1945;

/// <summary>
/// RFC 1945 — Security tests for HTTP/1.0 encoder.
/// Verifies header injection prevention and buffer overflow protection.
/// </summary>
public sealed class Http10EncoderSecurityTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    [Fact]
    public void HeaderInjection_CrInValue_ThrowsArgumentException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\rX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void HeaderInjection_LfInValue_ThrowsArgumentException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\nX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void HeaderInjection_CrLfInValue_ThrowsArgumentException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\r\nX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void HeaderInjection_Exception_ContainsHeaderName()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Dangerous", "bad\r\nvalue");

        var buffer = MakeBuffer();

        var ex = Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));

        Assert.Contains("X-Dangerous", ex.Message);
    }

    [Fact]
    public void HeaderInjection_NormalValue_DoesNotThrow()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Safe", "perfectly-normal-value-123");

        var buffer = MakeBuffer();
        var ex = Record.Exception(() => Http10Encoder.Encode(request, ref buffer));

        Assert.Null(ex);
    }

    [Fact]
    public void BufferOverflow_BufferTooSmallForHeaders_ThrowsInvalidOperationException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(5);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void BufferOverflow_BufferTooSmallForBody_ThrowsInvalidOperationException()
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

    [Fact]
    public void BufferOverflow_ExactSizeBuffer_DoesNotThrow()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var measureBuffer = MakeBuffer();
        var needed = Http10Encoder.Encode(request, ref measureBuffer);

        var exactBuffer = MakeBuffer(needed);
        var ex = Record.Exception(() => Http10Encoder.Encode(request, ref exactBuffer));

        Assert.Null(ex);
    }

    [Fact]
    public void BufferOverflow_EmptyBuffer_ThrowsInvalidOperationException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(0);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }
}
