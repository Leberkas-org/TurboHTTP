using System.Net;
using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class StatusLineWriterSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Writer_should_emit_canonical_status_line()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);
        StatusLineWriter.Write(ref writer, HttpVersion.Version11, 200, "OK");
        Assert.Equal("HTTP/1.1 200 OK\r\n", Encoding.ASCII.GetString(buffer, 0, writer.BytesWritten));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Writer_should_use_default_reason_phrase()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);
        StatusLineWriter.Write(ref writer, HttpVersion.Version11, 404);
        Assert.Equal("HTTP/1.1 404 Not Found\r\n", Encoding.ASCII.GetString(buffer, 0, writer.BytesWritten));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Writer_should_emit_http10_status_line()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);
        StatusLineWriter.Write(ref writer, HttpVersion.Version10, 301, "Moved Permanently");
        Assert.Equal("HTTP/1.0 301 Moved Permanently\r\n", Encoding.ASCII.GetString(buffer, 0, writer.BytesWritten));
    }

    [Fact(Timeout = 5000)]
    public void Writer_should_throw_when_buffer_too_small()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var buffer = new byte[5];
            var writer = SpanWriter.Create(buffer);
            StatusLineWriter.Write(ref writer, HttpVersion.Version11, 200, "OK");
        });
    }
}