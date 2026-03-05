using System.Buffers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11EncoderRangeRequestTests
{
    [Fact(DisplayName = "RFC7233-2.1: Range: bytes=0-499 encoded")]
    public void Test_Range_Bytes_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 499);
        var result = Encode(request);
        Assert.Contains("Range: bytes=0-499\r\n", result);
    }

    [Fact(DisplayName = "RFC7233-2.1: Range: bytes=-500 suffix encoded")]
    public void Test_Range_Suffix_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(null, 500);
        var result = Encode(request);
        Assert.Contains("Range: bytes=-500\r\n", result);
    }

    [Fact(DisplayName = "RFC7233-2.1: Range: bytes=500- open range encoded")]
    public void Test_Range_OpenEnded_Encoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/resource");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(500, null);
        var result = Encode(request);
        Assert.Contains("Range: bytes=500-\r\n", result);
    }

    [Fact(DisplayName = "RFC7233-2.1: Multi-range bytes=0-499,1000-1499 encoded")]
    public void Test_Range_MultiRange_Encoded()
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

    [Fact(DisplayName = "RFC7233-2.1: Invalid range bytes=abc-xyz rejected")]
    public void Test_Invalid_Range_Rejected()
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