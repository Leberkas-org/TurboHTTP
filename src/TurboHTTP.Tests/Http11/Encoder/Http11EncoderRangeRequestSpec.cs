using System.Buffers;
using System.Text;

namespace TurboHTTP.Tests.Http11.Encoder;

public sealed class Http11EncoderRangeRequestSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_range_header_when_byte_range()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 499);
        var result = Encode(request);
        Assert.Contains("Range: bytes=0-499\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_range_header_when_suffix_range()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(null, 500);
        var result = Encode(request);
        Assert.Contains("Range: bytes=-500\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_range_header_when_open_ended_range()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(500, null);
        var result = Encode(request);
        Assert.Contains("Range: bytes=500-\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_range_header_when_multi_range()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        var range = new System.Net.Http.Headers.RangeHeaderValue();
        range.Ranges.Add(new System.Net.Http.Headers.RangeItemHeaderValue(0, 499));
        range.Ranges.Add(new System.Net.Http.Headers.RangeItemHeaderValue(1000, 1499));
        request.Headers.Range = range;
        var result = Encode(request);
        Assert.Contains("Range: bytes=", result);
        Assert.Contains("0-499", result);
        Assert.Contains("1000-1499", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_reject_range_when_invalid()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=abc-xyz");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_reject_range_without_bytes_prefix()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "0-99");
        var buffer = new Memory<byte>(new byte[4096]);
        var threw = false;
        try
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        }
        catch (ArgumentException ex)
        {
            threw = ex.Message.Contains("bytes=");
        }

        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_reject_range_with_missing_dash()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0");
        var buffer = new Memory<byte>(new byte[4096]);
        var threw = false;
        try
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        }
        catch (ArgumentException ex)
        {
            threw = ex.Message.Contains("'-'");
        }

        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_reject_range_with_empty_spec()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=-");
        var buffer = new Memory<byte>(new byte[4096]);
        var threw = false;
        try
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        }
        catch (ArgumentException ex)
        {
            threw = ex.Message.Contains("empty range spec");
        }

        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_reject_range_with_non_digit_characters()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-a9");
        var buffer = new Memory<byte>(new byte[4096]);
        var threw = false;
        try
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        }
        catch (ArgumentException ex)
        {
            threw = ex.Message.Contains("non-digit");
        }

        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_accept_case_insensitive_bytes_prefix()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "BYTES=0-99");
        var result = Encode(request);
        Assert.Contains("Range: BYTES=0-99\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_accept_range_with_large_numbers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-9999999999");
        var result = Encode(request);
        Assert.Contains("Range: bytes=0-9999999999\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_accept_range_with_spaces()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-99, 200-299");
        var result = Encode(request);
        Assert.Contains("Range: bytes=0-99, 200-299\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_reject_range_with_invalid_character()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-9*");
        var buffer = new Memory<byte>(new byte[4096]);
        var threw = false;
        try
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        }
        catch (ArgumentException ex)
        {
            threw = ex.Message.Contains("non-digit");
        }

        Assert.True(threw);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.1.1")]
    public void Http11Encoder_should_reject_range_non_bytes_unit()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "units=0-99");
        var buffer = new Memory<byte>(new byte[4096]);
        var threw = false;
        try
        {
            var span = buffer.Span;
            TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        }
        catch (ArgumentException ex)
        {
            threw = ex.Message.Contains("bytes=");
        }

        Assert.True(threw);
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = TurboHTTP.Protocol.Http11.Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
