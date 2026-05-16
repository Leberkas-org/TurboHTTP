using System.Text;
using TurboHTTP.Protocol;

namespace TurboHTTP.Tests.Protocol;

public sealed class SpanWriterSpec
{
    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_bytes_and_advance_position()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteBytes("HTTP/1.1 "u8);

        Assert.Equal(9, writer.BytesWritten);
        Assert.Equal("HTTP/1.1 ", Encoding.ASCII.GetString(buffer.AsSpan(0, 9)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_ascii_string()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteAscii("GET");

        Assert.Equal(3, writer.BytesWritten);
        Assert.Equal("GET", Encoding.ASCII.GetString(buffer.AsSpan(0, 3)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_ascii_char_span()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteAscii("/path".AsSpan());

        Assert.Equal(5, writer.BytesWritten);
        Assert.Equal("/path", Encoding.ASCII.GetString(buffer.AsSpan(0, 5)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_crlf()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteCrlf();

        Assert.Equal(2, writer.BytesWritten);
        Assert.Equal((byte)'\r', buffer[0]);
        Assert.Equal((byte)'\n', buffer[1]);
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_integer_as_ascii_digits()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteInt(42);

        Assert.Equal(2, writer.BytesWritten);
        Assert.Equal("42", Encoding.ASCII.GetString(buffer.AsSpan(0, 2)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_zero_as_single_digit()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteInt(0);

        Assert.Equal(1, writer.BytesWritten);
        Assert.Equal("0", Encoding.ASCII.GetString(buffer.AsSpan(0, 1)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_hex_lowercase()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteHex(255);

        Assert.Equal(2, writer.BytesWritten);
        Assert.Equal("ff", Encoding.ASCII.GetString(buffer.AsSpan(0, 2)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_write_hex_zero()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteHex(0);

        Assert.Equal(1, writer.BytesWritten);
        Assert.Equal("0", Encoding.ASCII.GetString(buffer.AsSpan(0, 1)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_track_remaining_span()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteBytes("ABCD"u8);

        Assert.Equal(60, writer.Remaining.Length);
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_chain_multiple_writes()
    {
        var buffer = new byte[128];
        var writer = SpanWriter.Create(buffer);

        writer.WriteBytes("HTTP/1.1 "u8);
        writer.WriteInt(200);
        writer.WriteBytes(" "u8);
        writer.WriteAscii("OK");
        writer.WriteCrlf();

        Assert.Equal(17, writer.BytesWritten);
        Assert.Equal("HTTP/1.1 200 OK\r\n", Encoding.ASCII.GetString(buffer.AsSpan(0, 17)));
    }

    [Fact(Timeout = 5000)]
    public void SpanWriter_should_skip_empty_ascii_string()
    {
        var buffer = new byte[64];
        var writer = SpanWriter.Create(buffer);

        writer.WriteAscii(string.Empty);

        Assert.Equal(0, writer.BytesWritten);
    }
}