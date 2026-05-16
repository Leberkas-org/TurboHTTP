using System.Buffers;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class ContentLengthBufferedDecoderSpec
{
    [Fact(Timeout = 5000)]
    public async Task Decoder_should_complete_when_all_bytes_received_in_one_feed()
    {
        var decoder = new ContentLengthBufferedDecoder(5, MemoryPool<byte>.Shared);
        var done = decoder.Feed("hello"u8, out var consumed);

        Assert.True(done);
        Assert.Equal(5, consumed);
        var content = Assert.IsType<ReadOnlyMemoryContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, bytes.Length);
        Assert.Equal((byte)'h', bytes[0]);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_accumulate_across_feeds()
    {
        var decoder = new ContentLengthBufferedDecoder(5, MemoryPool<byte>.Shared);
        Assert.False(decoder.Feed("he"u8, out var c1));
        Assert.Equal(2, c1);
        Assert.True(decoder.Feed("llo!extra"u8, out var c2));
        Assert.Equal(3, c2);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_handle_zero_length_body()
    {
        var decoder = new ContentLengthBufferedDecoder(0, MemoryPool<byte>.Shared);
        Assert.True(decoder.Feed(ReadOnlySpan<byte>.Empty, out var consumed));
        Assert.Equal(0, consumed);
        Assert.IsType<ReadOnlyMemoryContent>(decoder.GetContent());
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Decoder_should_return_correct_bytes()
    {
        var decoder = new ContentLengthBufferedDecoder(3, MemoryPool<byte>.Shared);
        decoder.Feed("ab"u8, out _);
        decoder.Feed("cdef"u8, out _);
        var content = Assert.IsType<ReadOnlyMemoryContent>(decoder.GetContent());
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("abc"u8.ToArray(), bytes);
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void OnEof_should_return_false_when_incomplete()
    {
        var decoder = new ContentLengthBufferedDecoder(10, MemoryPool<byte>.Shared);
        decoder.Feed("short"u8, out _);
        Assert.False(decoder.OnEof());
        decoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void OnEof_should_return_true_when_complete()
    {
        var decoder = new ContentLengthBufferedDecoder(5, MemoryPool<byte>.Shared);
        decoder.Feed("hello"u8, out _);
        Assert.True(decoder.OnEof());
        decoder.Dispose();
    }
}