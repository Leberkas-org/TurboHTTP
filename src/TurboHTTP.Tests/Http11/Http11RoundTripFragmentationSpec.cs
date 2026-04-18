using System.Text;
using Decoder = TurboHTTP.Protocol.Http11.Decoder;

namespace TurboHTTP.Tests.Http11;

public sealed class Http11RoundTripFragmentationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_response_when_split_after_status_line()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(full);

        // "HTTP/1.1 200 OK\r\n" = 17 bytes
        const int splitAt = 17;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Decoder();
        var decoded1 = decoder.TryDecode(part1, out _);
        var decoded2 = decoder.TryDecode(part2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        Assert.Single(responses);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_response_when_split_at_header_body_boundary()
    {
        var headerBytes = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n"u8.ToArray();
        var bodyBytes = "hello"u8.ToArray();

        var decoder = new Decoder();
        decoder.TryDecode(headerBytes, out _);
        decoder.TryDecode(bodyBytes, out var responses);

        Assert.Single(responses);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_body_when_split_mid_body()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\n0123456789";
        var bytes = Encoding.ASCII.GetBytes(full);
        var headerLen = full.IndexOf("\r\n\r\n") + 4;

        // Split 5 bytes into the body
        var splitAt = headerLen + 5;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, splitAt);
        var part2 = new ReadOnlyMemory<byte>(bytes, splitAt, bytes.Length - splitAt);

        var decoder = new Decoder();
        decoder.TryDecode(part1, out _);
        decoder.TryDecode(part2, out var responses);

        Assert.Single(responses);
        Assert.Equal("0123456789", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11RoundTripFragmentation_should_assemble_response_when_single_byte_tcp_delivery()
    {
        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\nabc";
        var bytes = Encoding.ASCII.GetBytes(full);

        var decoder = new Decoder();
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
        Assert.Equal("abc", await finalResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11RoundTripFragmentation_should_assemble_chunked_body_when_split_between_chunks()
    {
        var part1 = (ReadOnlyMemory<byte>)"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nfoo\r\n"u8.ToArray();
        var part2 = (ReadOnlyMemory<byte>)"3\r\nbar\r\n0\r\n\r\n"u8.ToArray();

        var decoder = new Decoder();
        decoder.TryDecode(part1, out _);
        decoder.TryDecode(part2, out var responses);

        Assert.Single(responses);
        Assert.Equal("foobar", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
