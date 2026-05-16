using System.Text;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ChunkedBodyDecoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public async Task Decoder_should_decode_two_chunks_and_terminator()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.1")]
    public async Task Decoder_should_ignore_chunk_extensions()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5;ext=foo\r\nhello\r\n0\r\n\r\n"u8.ToArray();
        Assert.True(decoder.Feed(data, out _));

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1")]
    public void Decoder_should_signal_NeedMore_when_chunk_incomplete()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "5\r\nhel"u8.ToArray();
        Assert.False(decoder.Feed(data, out _));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_reject_invalid_chunk_size()
    {
        var decoder = new ChunkedBodyDecoder();
        var data = "XYZ\r\n"u8.ToArray();
        Assert.Throws<HttpProtocolException>(() => decoder.Feed(data, out _));
        decoder.Dispose();
    }
}