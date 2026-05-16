using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class RequestLineWriterSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Writer_should_emit_canonical_request_line()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);
        RequestLineWriter.Write(ref writer, "GET", "/foo", HttpVersion.Version11);
        Assert.Equal("GET /foo HTTP/1.1\r\n", Encoding.ASCII.GetString(buffer, 0, writer.BytesWritten));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void Writer_should_emit_http10_request_line()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);
        RequestLineWriter.Write(ref writer, "POST", "/submit", HttpVersion.Version10);
        Assert.Equal("POST /submit HTTP/1.0\r\n", Encoding.ASCII.GetString(buffer, 0, writer.BytesWritten));
    }

    [Fact(Timeout = 5000)]
    public void Writer_should_throw_when_buffer_too_small()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var buffer = new byte[5];
            var writer = SpanWriter.Create(buffer);
            RequestLineWriter.Write(ref writer, "GET", "/", HttpVersion.Version11);
        });
    }
}