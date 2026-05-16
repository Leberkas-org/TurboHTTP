using System.Text;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ContentLengthStreamedDecoderSpec
{
    [Fact(Timeout = 5000)]
    public async Task Decoder_should_stream_bytes_through_pipe()
    {
        var decoder = new ContentLengthStreamedDecoder(11);
        Assert.False(decoder.Feed("hello "u8, out var c1));
        Assert.Equal(6, c1);
        Assert.True(decoder.Feed("world"u8, out var c2));
        Assert.Equal(5, c2);

        var content = Assert.IsType<StreamContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello world", Encoding.ASCII.GetString(bytes));
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_consume_only_needed_bytes()
    {
        var decoder = new ContentLengthStreamedDecoder(3);
        Assert.True(decoder.Feed("abcdef"u8, out var consumed));
        Assert.Equal(3, consumed);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void OnEof_should_return_false_when_incomplete()
    {
        var decoder = new ContentLengthStreamedDecoder(10);
        decoder.Feed("short"u8, out _);
        Assert.False(decoder.OnEof());
        decoder.Dispose();
    }
}