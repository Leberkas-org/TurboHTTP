using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class Http11ClientDecoderSpec
{
    private readonly Http11ClientDecoder _decoder = new(Http11ClientDecoderOptions.Default);

    [Fact(Timeout = 5000)]
    public void Feed_should_decode_simple_response()
    {
        const string response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nHello";
        var bytes = Encoding.ASCII.GetBytes(response);

        var outcome = _decoder.Feed(bytes, requestMethodWasHead: false, out var consumed);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        Assert.Equal(bytes.Length, consumed);

        var msg = _decoder.GetResponse();
        Assert.Equal(200, (int)msg.StatusCode);
        Assert.Equal("OK", msg.ReasonPhrase);
        Assert.Equal(new Version(1, 1), msg.Version);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_handle_multiple_headers()
    {
        const string response = "HTTP/1.1 200 OK\r\n" +
                                "Content-Type: text/plain\r\n" +
                                "Content-Length: 0\r\n" +
                                "Server: TurboHTTP\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(response);

        var outcome = _decoder.Feed(bytes, requestMethodWasHead: false, out var consumed);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        var msg = _decoder.GetResponse();
        Assert.True(msg.Headers.Contains("Server"));
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_state()
    {
        const string response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(response);

        _decoder.Feed(bytes, requestMethodWasHead: false, out _);
        var first = _decoder.GetResponse();

        _decoder.Reset();

        const string emptyResponse = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n";
        var emptyBytes = Encoding.ASCII.GetBytes(emptyResponse);
        _decoder.Feed(emptyBytes, requestMethodWasHead: false, out _);
        var second = _decoder.GetResponse();

        Assert.NotEqual((int)first.StatusCode, (int)second.StatusCode);
    }
}