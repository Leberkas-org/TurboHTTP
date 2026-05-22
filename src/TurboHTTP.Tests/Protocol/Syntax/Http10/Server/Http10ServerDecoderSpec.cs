using System.Text;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http10.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerDecoderSpec
{
    private static Http10ServerDecoder MakeDecoder() => new(Http10ServerDecoderOptions.Default);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Decoder_should_parse_simple_get_request()
    {
        var raw = "GET /foo HTTP/1.0\r\nUser-Agent: t\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, out _));

        var request = decoder.GetRequest();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/foo", request.RequestUri?.OriginalString);
        Assert.True(request.Headers.Contains("User-Agent"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public async Task Decoder_should_buffer_post_body_with_content_length()
    {
        var raw = "POST /submit HTTP/1.0\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, out _));

        var request = decoder.GetRequest();
        var bytes = await request.Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_signal_NeedMore_for_incomplete_request_line()
    {
        var partial = "GET /fo"u8.ToArray();
        Assert.Equal(DecodeOutcome.NeedMore, MakeDecoder().Feed(partial, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1")]
    public void Decoder_should_reject_embedded_crlf_in_request_line()
    {
        var raw = "GET /pa\r\nth HTTP/1.0\r\nHost: x\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        var result = decoder.Feed(raw, out _);

        Assert.NotEqual(DecodeOutcome.Complete, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1.2")]
    public void Decoder_should_preserve_percent_encoded_request_uri()
    {
        var raw = "GET /hello%20world HTTP/1.0\r\nHost: x\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, out _));

        var request = decoder.GetRequest();
        Assert.Contains("%20", request.RequestUri?.OriginalString ?? "");
    }

    [Fact(Timeout = 5000)]
    public void GetRequestFeature_should_parse_method_and_path()
    {
        var decoder = MakeDecoder();
        var data = "GET /hello?q=1 HTTP/1.0\r\nHost: example.com\r\n\r\n"u8.ToArray();
        var outcome = decoder.Feed(data, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);

        var feature = decoder.GetRequestFeature();

        Assert.Equal("GET", feature.Method);
        Assert.Equal("/hello", feature.Path);
        Assert.Equal("?q=1", feature.QueryString);
        Assert.Equal("/hello?q=1", feature.RawTarget);
        Assert.Equal("HTTP/1.0", feature.Protocol);
        Assert.Equal("example.com", feature.Headers["Host"]);
    }
}