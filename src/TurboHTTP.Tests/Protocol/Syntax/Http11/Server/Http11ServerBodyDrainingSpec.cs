using System.Buffers;
using System.Text;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerBodyDrainingSpec
{
    [Fact(Timeout = 5000)]
    public void ContentLengthBufferedDecoder_IsComplete_should_return_true_when_all_bytes_received()
    {
        var decoder = new ContentLengthBufferedDecoder(10, MemoryPool<byte>.Shared);

        var data = "0123456789"u8.ToArray();
        decoder.Feed(data, out _);

        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthBufferedDecoder_IsComplete_should_return_false_when_incomplete()
    {
        var decoder = new ContentLengthBufferedDecoder(10, MemoryPool<byte>.Shared);

        var data = "01234"u8.ToArray();
        decoder.Feed(data, out _);

        Assert.False(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthBufferedDecoder_Drain_should_skip_remaining_bytes()
    {
        var decoder = new ContentLengthBufferedDecoder(10, MemoryPool<byte>.Shared);

        var data = "012"u8.ToArray();
        decoder.Feed(data, out _);
        Assert.False(decoder.IsComplete);

        var remaining = "3456789"u8.ToArray();
        var drained = decoder.Drain(remaining);

        Assert.Equal(7, drained);
        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthBufferedDecoder_Drain_should_return_zero_when_complete()
    {
        var decoder = new ContentLengthBufferedDecoder(5, MemoryPool<byte>.Shared);

        var data = "01234"u8.ToArray();
        decoder.Feed(data, out _);
        Assert.True(decoder.IsComplete);

        var drained = decoder.Drain("extra"u8);

        Assert.Equal(0, drained);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthBufferedDecoder_Drain_should_consume_only_needed_bytes()
    {
        var decoder = new ContentLengthBufferedDecoder(10, MemoryPool<byte>.Shared);

        var data = "01234"u8.ToArray();
        decoder.Feed(data, out _);

        var remaining = "567890extra"u8.ToArray();
        var drained = decoder.Drain(remaining);

        Assert.Equal(5, drained);
        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthStreamedDecoder_IsComplete_should_return_true_when_all_bytes_received()
    {
        var decoder = new ContentLengthStreamedDecoder(10);

        var data = "0123456789"u8.ToArray();
        decoder.Feed(data, out _);

        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthStreamedDecoder_IsComplete_should_return_false_when_incomplete()
    {
        var decoder = new ContentLengthStreamedDecoder(10);

        var data = "01234"u8.ToArray();
        decoder.Feed(data, out _);

        Assert.False(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthStreamedDecoder_Drain_should_skip_remaining_bytes()
    {
        var decoder = new ContentLengthStreamedDecoder(10);

        var data = "012"u8.ToArray();
        decoder.Feed(data, out _);
        Assert.False(decoder.IsComplete);

        var remaining = "3456789"u8.ToArray();
        var drained = decoder.Drain(remaining);

        Assert.Equal(7, drained);
        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ContentLengthStreamedDecoder_Drain_should_return_zero_when_complete()
    {
        var decoder = new ContentLengthStreamedDecoder(5);

        var data = "01234"u8.ToArray();
        decoder.Feed(data, out _);
        Assert.True(decoder.IsComplete);

        var drained = decoder.Drain("extra"u8);

        Assert.Equal(0, drained);
    }

    [Fact(Timeout = 5000)]
    public void ChunkedBodyDecoder_IsComplete_should_return_true_when_chunk_stream_complete()
    {
        var decoder = new ChunkedBodyDecoder();

        var chunks = "5\r\nhello\r\n0\r\n\r\n"u8;
        decoder.Feed(chunks, out _);

        Assert.True(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ChunkedBodyDecoder_IsComplete_should_return_false_when_incomplete()
    {
        var decoder = new ChunkedBodyDecoder();

        var chunks = "5\r\nhello"u8;
        decoder.Feed(chunks, out _);

        Assert.False(decoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void ChunkedBodyDecoder_Drain_should_parse_and_skip_remaining_chunks()
    {
        var decoder = new ChunkedBodyDecoder();

        var partial = "5\r\nhello\r\n"u8;
        decoder.Feed(partial, out _);
        Assert.False(decoder.IsComplete);

        var remaining = "5\r\nworld\r\n0\r\n\r\n"u8;
        var drained = decoder.Drain(remaining);

        Assert.True(decoder.IsComplete);
        Assert.True(drained > 0);
    }

    [Fact(Timeout = 5000)]
    public void ChunkedBodyDecoder_Drain_should_return_zero_when_complete()
    {
        var decoder = new ChunkedBodyDecoder();

        var chunks = "5\r\nhello\r\n0\r\n\r\n"u8;
        decoder.Feed(chunks, out _);
        Assert.True(decoder.IsComplete);

        var drained = decoder.Drain("extra"u8);

        Assert.Equal(0, drained);
    }

    [Fact(Timeout = 5000)]
    public void Http11ServerStateMachine_should_expose_current_body_decoder()
    {
        var decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);

        const string request = "POST / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(request);

        decoder.Feed(bytes, out _);

        Assert.NotNull(decoder.CurrentBodyDecoder);
        Assert.True(decoder.CurrentBodyDecoder.IsComplete);
    }

    [Fact(Timeout = 5000)]
    public void Http11ServerStateMachine_should_expose_null_body_decoder_when_reset()
    {
        var decoder = new Http11ServerDecoder(Http11ServerDecoderOptions.Default);

        const string request = "POST / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(request);

        decoder.Feed(bytes, out _);
        decoder.Reset();

        Assert.Null(decoder.CurrentBodyDecoder);
    }
}