using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Tests.Http10;

/// <summary>
/// Tests HTTP/1.0 header-injection protection per RFC 1945 §12.
/// Verifies that CR, LF, and CRLF sequences in header values are rejected.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Encoder"/>.
/// RFC 1945 §12: Security considerations — header injection prevention.
/// </remarks>
public sealed class Http10EncoderSecuritySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_throw_argument_exception_when_header_value_contains_cr()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\rX-Injected: attack");

        var threw = false;
        try
        {
            Span<byte> buffer = new byte[8192];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_throw_argument_exception_when_header_value_contains_lf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\nX-Injected: attack");

        var threw = false;
        try
        {
            Span<byte> buffer = new byte[8192];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_throw_argument_exception_when_header_value_contains_crlf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\r\nX-Injected: attack");

        var threw = false;
        try
        {
            Span<byte> buffer = new byte[8192];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_include_header_name_in_exception_when_injection_detected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Dangerous", "bad\r\nvalue");

        ArgumentException? ex = null;
        try
        {
            Span<byte> buffer = new byte[8192];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (ArgumentException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Contains("X-Dangerous", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_not_throw_when_header_value_is_normal()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Safe", "perfectly-normal-value-123");

        Exception? ex = null;
        try
        {
            Span<byte> buffer = new byte[8192];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_throw_invalid_operation_exception_when_buffer_too_small_for_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var threw = false;
        try
        {
            Span<byte> buffer = new byte[5];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_throw_invalid_operation_exception_when_buffer_too_small_for_body()
    {
        var largeBody = new byte[1000];
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var threw = false;
        try
        {
            Span<byte> buffer = new byte[100];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_not_throw_when_buffer_is_exact_size()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        Span<byte> measureBuffer = new byte[8192];
        var needed = Http10Encoder.Encode(request, ref measureBuffer);

        Exception? ex = null;
        try
        {
            Span<byte> exactBuffer = new byte[needed];
            Http10Encoder.Encode(request, ref exactBuffer);
        }
        catch (Exception e)
        {
            ex = e;
        }

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-12")]
    public void Http10EncoderSecurity_should_throw_invalid_operation_exception_when_buffer_is_empty()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var threw = false;
        try
        {
            Span<byte> buffer = new byte[0];
            Http10Encoder.Encode(request, ref buffer);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.True(threw);
    }
}
