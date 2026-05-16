using System.Text;
using Akka.Actor;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

public sealed class Http11ClientEncoderSpec
{
    private readonly Http11ClientEncoder _encoder = new(Http11ClientEncoderOptions.Default);

    [Fact(Timeout = 5000)]
    public void Encode_should_write_request_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        Assert.True(written > 0);
        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("GET /path HTTP/1.1", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_add_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/path");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Host: example.com:8080", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_write_headers_with_content_length()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("test body"u8.ToArray())
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        Assert.True(written > 0);
        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("POST / HTTP/1.1", result);
        Assert.Contains("Content-Length: 9", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_write_connection_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Connection:", result);
    }
}