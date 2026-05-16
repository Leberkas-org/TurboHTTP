using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class HeaderBlockReaderSpec
{
    private static HeaderBlockReader MakeReader() =>
        new(maxHeaderBytes: 32 * 1024, maxHeaderCount: 100, maxLineLength: 8 * 1024, allowObsFold: false);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void Reader_should_parse_complete_block_in_one_feed()
    {
        var raw = "Host: example.com\r\nUser-Agent: test\r\n\r\n"u8.ToArray();
        var reader = MakeReader();

        var result = reader.Feed(raw, out var consumed);
        Assert.Equal(HeaderBlockResult.Complete, result);
        Assert.Equal(raw.Length, consumed);
        var headers = reader.GetHeaders();
        Assert.Equal("example.com", headers.GetCombined("Host"));
        Assert.Equal("test", headers.GetCombined("User-Agent"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void Reader_should_signal_need_more_when_incomplete()
    {
        var partial = "Host: example.com\r\nUser-Ag"u8.ToArray();
        var reader = MakeReader();

        Assert.Equal(HeaderBlockResult.NeedMore, reader.Feed(partial, out var consumed));
        Assert.Equal(19, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5.5")]
    public void Reader_should_reject_obs_fold_by_default()
    {
        var raw = "X-Foo: a\r\n b\r\n\r\n"u8.ToArray();
        var reader = MakeReader();
        Assert.Throws<HttpProtocolException>(() => reader.Feed(raw, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void Reader_should_reject_when_max_header_count_exceeded()
    {
        var reader = new HeaderBlockReader(32 * 1024, maxHeaderCount: 2, 8 * 1024, false);
        var raw = "A: 1\r\nB: 2\r\nC: 3\r\n\r\n"u8.ToArray();
        Assert.Throws<HttpProtocolException>(() => reader.Feed(raw, out _));
    }

    [Fact(Timeout = 5000)]
    public void Reader_should_handle_multiple_values_for_same_header()
    {
        var raw = "Accept: text/html\r\nAccept: application/json\r\n\r\n"u8.ToArray();
        var reader = MakeReader();
        reader.Feed(raw, out _);
        var headers = reader.GetHeaders();
        Assert.Equal("text/html, application/json", headers.GetCombined("Accept"));
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_state()
    {
        var raw = "Host: a\r\n\r\n"u8.ToArray();
        var reader = MakeReader();
        reader.Feed(raw, out _);
        reader.Reset();
        Assert.Equal(0, reader.GetHeaders().Count);
    }
}