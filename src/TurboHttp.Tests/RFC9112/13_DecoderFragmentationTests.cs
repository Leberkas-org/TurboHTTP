using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9112;

public sealed class Http11DecoderFragmentationTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact(DisplayName = "RFC9112: Status-line split byte 1 reassembled")]
    public async Task StatusLine_SplitAtByte1_Reassembled()
    {
        var full = BuildResponse(200, "OK", "body", ("Content-Length", "4"));
        var chunk1 = full[..1];
        var chunk2 = full[1..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("body", result);
    }

    [Fact(DisplayName = "RFC9112: Status-line split inside HTTP/1.1 version")]
    public async Task StatusLine_SplitInsideVersion_Reassembled()
    {
        var full = BuildResponse(200, "OK", "data", ("Content-Length", "4"));
        var chunk1 = full[..10]; // Split inside "HTTP/1.1"
        var chunk2 = full[10..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("data", result);
    }

    [Fact(DisplayName = "RFC9112: Header name:value split at colon")]
    public async Task Header_SplitAtColon_Reassembled()
    {
        var full = BuildResponse(200, "OK", "test", ("Content-Length", "4"), ("X-Custom", "value"));
        var colonPos = Encoding.UTF8.GetString(full.Span).IndexOf("X-Custom:", StringComparison.Ordinal) + 8;
        var chunk1 = full[..colonPos];
        var chunk2 = full[colonPos..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("test", result);
    }

    [Fact(DisplayName = "RFC9112: Split at CRLFCRLF header-body boundary")]
    public async Task Split_AtHeaderBodyBoundary_Reassembled()
    {
        const string body = "complete";
        var full = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));
        var headerEnd = IndexOfDoubleCrlf(full) + 2; // Split in middle of \r\n\r\n
        var chunk1 = full[..headerEnd];
        var chunk2 = full[headerEnd..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(body, result);
    }

    [Fact(DisplayName = "RFC9112: Chunk-size line split across two reads")]
    public async Task ChunkSize_SplitAcrossReads_Reassembled()
    {
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var full = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var headerEnd = IndexOfDoubleCrlf(full) + 4;
        var chunk1 = full[..(headerEnd + 1)]; // Split after "5" chunk size
        var chunk2 = full[(headerEnd + 1)..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact(DisplayName = "RFC9112: Response delivered 1 byte at a time assembles correctly")]
    public async Task Response_OneByteAtATime_AssemblesCorrectly()
    {
        const string body = "OK";
        var full = BuildResponse(200, "OK", body, ("Content-Length", "2"));

        // Send one byte at a time
        for (int i = 0; i < full.Length - 1; i++)
        {
            var chunk = full.Slice(i, 1);
            var decoded = _decoder.TryDecode(chunk, out _);
            Assert.False(decoded, $"Should not decode until all bytes received (byte {i})");
        }

        // Send final byte
        var finalChunk = full.Slice(full.Length - 1, 1);
        var finalDecoded = _decoder.TryDecode(finalChunk, out var responses);

        Assert.True(finalDecoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(body, result);
    }

    private static ReadOnlyMemory<byte> BuildResponse(int code, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static ReadOnlyMemory<byte> BuildRaw(int code, string reason, string rawBody,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(rawBody);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static int IndexOfDoubleCrlf(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        for (var i = 0; i <= span.Length - 4; i++)
        {
            if (span[i] == '\r' && span[i + 1] == '\n' && span[i + 2] == '\r' && span[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }
}
