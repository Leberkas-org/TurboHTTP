using System.Text;
using Akka.Actor;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class Http11ServerEncoderSpec
{
    private readonly Http11ServerEncoder _encoder = new(Http11ServerEncoderOptions.Default);

    [Fact(Timeout = 5000)]
    public void Encode_should_write_status_line()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        Assert.True(written > 0);
        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("HTTP/1.1 200 OK", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_add_content_length()
    {
        var body = "test body"u8.ToArray();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Content-Length: 9", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_handle_chunked_response()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("chunked"u8.ToArray()),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: true);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("HTTP/1.1 200 OK", result);
        Assert.DoesNotContain("Content-Length", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_include_date_header()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Date:", result);
    }
}