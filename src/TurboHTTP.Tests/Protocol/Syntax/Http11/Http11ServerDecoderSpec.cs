using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class Http11ServerDecoderSpec
{
    private readonly Http11ServerDecoder _decoder = new(Http11ServerDecoderOptions.Default);

    [Fact(Timeout = 5000)]
    public void Feed_should_decode_simple_request()
    {
        const string request = "GET /path HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = _decoder.Feed(bytes, out var consumed);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        Assert.Equal(bytes.Length, consumed);

        var msg = _decoder.GetRequest();
        Assert.Equal(HttpMethod.Get, msg.Method);
        Assert.Equal("/path", msg.RequestUri?.OriginalString);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_handle_post_with_body()
    {
        const string body = "test data";
        var request = $"POST / HTTP/1.1\r\n" +
                      $"Host: example.com\r\n" +
                      $"Content-Length: {body.Length}\r\n\r\n" +
                      body;
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = _decoder.Feed(bytes, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        var msg = _decoder.GetRequest();
        Assert.Equal(HttpMethod.Post, msg.Method);
        Assert.NotNull(msg.Content);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_return_need_more_for_incomplete()
    {
        const string request = "GET /path HTTP/1.1\r\nHost: example.com\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);

        var outcome = _decoder.Feed(bytes, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_state()
    {
        const string request1 = "GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n";
        var bytes1 = Encoding.ASCII.GetBytes(request1);

        _decoder.Feed(bytes1, out _);
        var first = _decoder.GetRequest();

        _decoder.Reset();

        const string request2 = "POST / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n";
        var bytes2 = Encoding.ASCII.GetBytes(request2);
        _decoder.Feed(bytes2, out _);
        var second = _decoder.GetRequest();

        Assert.NotEqual(first.Method, second.Method);
    }
}