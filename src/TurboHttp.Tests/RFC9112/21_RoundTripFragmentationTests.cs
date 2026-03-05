using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11RoundTripFragmentationTests
{
    [Fact(DisplayName = "RFC9112-4: TCP fragment split after status line CRLF — response assembled")]
    public async Task Should_AssembleResponse_When_SplitAfterStatusLine()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(full);

        // "HTTP/1.1 200 OK\r\n" = 17 bytes
        const int splitAt = 17;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Http11Decoder();
        var decoded1 = decoder.TryDecode(part1, out _);
        var decoded2 = decoder.TryDecode(part2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        Assert.Single(responses);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-4: TCP fragment split at header-body boundary — response assembled")]
    public async Task Should_AssembleResponse_When_SplitAtHeaderBodyBoundary()
    {
        var headerBytes = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"u8.ToArray();
        var bodyBytes = "hello"u8.ToArray();

        var decoder = new Http11Decoder();
        decoder.TryDecode(headerBytes, out _);
        decoder.TryDecode(bodyBytes, out var responses);

        Assert.Single(responses);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-4: TCP fragment split mid-body — body assembled correctly")]
    public async Task Should_AssembleBody_When_SplitMidBody()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\n0123456789";
        var bytes = Encoding.ASCII.GetBytes(full);
        var headerLen = full.IndexOf("\r\n\r\n") + 4;

        // Split 5 bytes into the body
        var splitAt = headerLen + 5;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Http11Decoder();
        decoder.TryDecode(part1, out _);
        decoder.TryDecode(part2, out var responses);

        Assert.Single(responses);
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-4: Single-byte TCP delivery assembles complete response")]
    public async Task Should_AssembleResponse_When_SingleByteTcpDelivery()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\nabc";
        var bytes = Encoding.ASCII.GetBytes(full);

        var decoder = new Http11Decoder();
        HttpResponseMessage? finalResponse = null;

        for (var i = 0; i < bytes.Length; i++)
        {
            var chunk = new ReadOnlyMemory<byte>(bytes, i, 1);
            if (decoder.TryDecode(chunk, out var r) && r.Count > 0)
            {
                finalResponse = r[0];
            }
        }

        Assert.NotNull(finalResponse);
        Assert.Equal("abc", await finalResponse.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "RFC9112-6: TCP fragment split between two chunks — body assembled correctly")]
    public async Task Should_AssembleChunkedBody_When_SplitBetweenChunks()
    {
        var part1 = (ReadOnlyMemory<byte>)"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nfoo\r\n"u8.ToArray();
        var part2 = (ReadOnlyMemory<byte>)"3\r\nbar\r\n0\r\n\r\n"u8.ToArray();

        var decoder = new Http11Decoder();
        decoder.TryDecode(part1, out _);
        decoder.TryDecode(part2, out var responses);

        Assert.Single(responses);
        Assert.Equal("foobar", await responses[0].Content.ReadAsStringAsync());
    }
}
