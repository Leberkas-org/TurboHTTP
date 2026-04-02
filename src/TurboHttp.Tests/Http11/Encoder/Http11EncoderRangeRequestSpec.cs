using System.Buffers;
using System.Text;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Tests.Http11.Encoder;

/// <summary>
/// Tests Range request header encoding per RFC 9112 §5.
/// Verifies byte-range and multi-range header serialization.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http11Encoder"/>.
/// RFC 9112 §5 / RFC 9110 §14.2: Range header — bytes=first-byte-pos "-" last-byte-pos.
/// </remarks>
public sealed class Http11EncoderRangeRequestSpec
{
    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_range_header_when_byte_range()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 499);
        var result = Encode(request);
        Assert.Contains("Range: bytes=0-499\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_range_header_when_suffix_range()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(null, 500);
        var result = Encode(request);
        Assert.Contains("Range: bytes=-500\r\n", result);
    }

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_encode_range_header_when_open_ended_range()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(500, null);
        var result = Encode(request);
        Assert.Contains("Range: bytes=500-\r\n", result);
    }

    [Fact]
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

    [Fact]
    [Trait("RFC", "RFC9112-5")]
    public void Http11Encoder_should_reject_range_when_invalid()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.TryAddWithoutValidation("Range", "bytes=abc-xyz");
        var buffer = new Memory<byte>(new byte[4096]);
        Assert.Throws<ArgumentException>(() =>
        {
            var span = buffer.Span;
            Http11Encoder.Encode(request, ref span);
        });
    }

    private static string Encode(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
