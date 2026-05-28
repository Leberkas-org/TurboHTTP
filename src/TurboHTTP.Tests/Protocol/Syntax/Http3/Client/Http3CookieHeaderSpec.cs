using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3CookieHeaderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void QpackEncoder_should_encode_cookie_headers_independently()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var headers = new List<(string Name, string Value)>
        {
            (":status", "200"),
            ("cookie", "a=1"),
            ("cookie", "b=2"),
        };
        var encoded = encoder.Encode(headers);
        var decoder = new QpackDecoder(maxTableCapacity: 0);
        var decoded = decoder.Decode(encoded.Span);
        var cookieHeaders = decoded.Where(h => h.Name == "cookie").ToList();
        Assert.Equal(2, cookieHeaders.Count);
        Assert.Equal("a=1", cookieHeaders[0].Value);
        Assert.Equal("b=2", cookieHeaders[1].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void ResponseDecoder_should_accept_single_cookie_header()
    {
        var tableSync = new QpackTableSync();
        var decoder = new Http3ClientDecoder(tableSync);
        var frame = new HeadersFrame(tableSync.Encoder.Encode([
            (":status", "200"),
            ("cookie", "session=abc123")
        ]));
        var state = new StreamState();
        decoder.DecodeHeaders(frame, state);
        Assert.True(state.HasResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.2")]
    public void ResponseDecoder_should_accept_multiple_cookie_headers()
    {
        var tableSync = new QpackTableSync();
        var decoder = new Http3ClientDecoder(tableSync);
        var frame = new HeadersFrame(tableSync.Encoder.Encode([
            (":status", "200"),
            ("cookie", "a=1"),
            ("cookie", "b=2"),
            ("cookie", "c=3")
        ]));
        var state = new StreamState();
        decoder.DecodeHeaders(frame, state);
        Assert.True(state.HasResponse);
    }
}