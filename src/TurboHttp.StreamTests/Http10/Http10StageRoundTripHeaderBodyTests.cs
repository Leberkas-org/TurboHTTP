using System.Buffers;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http10;

/// <summary>
/// RFC 1945 — Round-trip header and body tests through Akka.Streams stages.
/// Verifies that bodies (empty, large, binary) and custom headers survive
/// the Http10EncoderStage → wire → Http10DecoderStage cycle.
/// </summary>
public sealed class Http10StageRoundTripHeaderBodyTests : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var sb = new StringBuilder();
        foreach (var item in chunks)
        {
            var data = (DataItem)item;
            sb.Append(Encoding.Latin1.GetString(data.Memory.Memory.Span[..data.Length]));
            data.Memory.Dispose();
        }

        return sb.ToString();
    }

    private async Task<byte[]> EncodeRawAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        using var ms = new MemoryStream();
        foreach (var item in chunks)
        {
            var data = (DataItem)item;
            ms.Write(data.Memory.Memory.Span[..data.Length]);
            data.Memory.Dispose();
        }

        return ms.ToArray();
    }

    private static IInputItem Chunk(byte[] data)
        => new DataItem(HostKey.Default, new SimpleMemoryOwner(data), data.Length);

    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return new DataItem(HostKey.Default, new SimpleMemoryOwner(bytes), bytes.Length);
    }

    private async Task<HttpResponseMessage> DecodeAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    private async Task<HttpResponseMessage> DecodeRawAsync(byte[] data)
    {
        var source = Source.Single(Chunk(data));
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-10RT-B-001: Empty body → Content-Length: 0")]
    public async Task ST_10RT_B_001_Empty_Body_ContentLength_Zero()
    {
        // Encode a POST with empty content
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/empty")
        {
            Version = HttpVersion.Version10,
            Content = new ByteArrayContent([])
        };
        var wire = await EncodeAsync(request);

        Assert.StartsWith("POST /empty HTTP/1.0\r\n", wire);
        Assert.Contains("Content-Length: 0", wire);

        // Decode matching response with empty body
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(Timeout = 30_000, DisplayName = "RFC-1945-§8-10RT-B-002: Large body (64 KB) → correctly serialized and deserialized")]
    public async Task ST_10RT_B_002_Large_Body_64KB()
    {
        // Build a 64 KB payload
        var payload = new string('A', 65536);
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/large")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(payload, Encoding.Latin1, "text/plain")
        };
        var wire = await EncodeAsync(request);

        Assert.StartsWith("POST /large HTTP/1.0\r\n", wire);
        Assert.Contains("Content-Length: 65536", wire);

        // Extract body from wire
        var separatorIdx = wire.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx >= 0, "Missing header/body separator");
        var bodyPart = wire[(separatorIdx + 4)..];
        Assert.Equal(65536, bodyPart.Length);
        Assert.True(bodyPart.All(c => c == 'A'));

        // Decode a 64 KB response
        var responsePayload = new string('B', 65536);
        var response = await DecodeAsync(
            $"HTTP/1.0 200 OK\r\nContent-Length: 65536\r\n\r\n{responsePayload}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var respBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(65536, respBody.Length);
        Assert.True(respBody.All(c => c == 'B'));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-10RT-B-003: Binary body (bytes 0x00–0xFF) → byte-for-byte identical")]
    public async Task ST_10RT_B_003_Binary_Body_ByteForByte()
    {
        // Build a 256-byte binary payload (0x00..0xFF)
        var binaryPayload = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            binaryPayload[i] = (byte)i;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/binary")
        {
            Version = HttpVersion.Version10,
            Content = new ByteArrayContent(binaryPayload)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var rawWire = await EncodeRawAsync(request);

        // Find the separator in raw bytes
        var separator = "\r\n\r\n"u8.ToArray();
        var sepIdx = FindBytes(rawWire, separator);
        Assert.True(sepIdx >= 0, "Missing header/body separator in raw wire");
        var wireBody = rawWire[(sepIdx + 4)..];
        Assert.Equal(binaryPayload, wireBody);

        // Decode a binary response
        var responseHeader = Encoding.ASCII.GetBytes(
            $"HTTP/1.0 200 OK\r\nContent-Length: {binaryPayload.Length}\r\n\r\n");
        var responseData = new byte[responseHeader.Length + binaryPayload.Length];
        responseHeader.CopyTo(responseData, 0);
        binaryPayload.CopyTo(responseData, responseHeader.Length);

        var response = await DecodeRawAsync(responseData);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var respBody = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(binaryPayload, respBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-10RT-B-004: Custom headers in request → present in wire format")]
    public async Task ST_10RT_B_004_Custom_Request_Headers_In_Wire()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.Add("X-Request-Id", "abc-123");
        request.Headers.Add("X-Trace-Token", "trace-456");
        request.Headers.Add("Accept", "application/json");

        var wire = await EncodeAsync(request);

        Assert.StartsWith("GET /api HTTP/1.0\r\n", wire);
        // HttpRequestMessage normalizes header names (e.g. X-Request-Id → X-Request-ID)
        // so we check case-insensitively for the header name and exact value
        var wireLower = wire.ToLowerInvariant();
        Assert.Contains("x-request-id: abc-123", wireLower);
        Assert.Contains("x-trace-token: trace-456", wireLower);
        Assert.Contains("accept: application/json", wireLower);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-10RT-B-005: Response with multiple headers → all correctly parsed")]
    public async Task ST_10RT_B_005_Response_Multiple_Headers()
    {
        var response = await DecodeAsync(
            "HTTP/1.0 200 OK\r\n" +
            "Server: TurboHttp/1.0\r\n" +
            "X-Request-Id: req-789\r\n" +
            "X-Powered-By: Tests\r\n" +
            "Content-Type: text/html\r\n" +
            "Content-Length: 13\r\n" +
            "\r\n" +
            "Hello, World!");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Check response headers
        Assert.Equal("TurboHttp/1.0", response.Headers.GetValues("Server").Single());
        Assert.Equal("req-789", response.Headers.GetValues("X-Request-Id").Single());
        Assert.Equal("Tests", response.Headers.GetValues("X-Powered-By").Single());

        // Check content headers
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(13, response.Content.Headers.ContentLength);

        // Check body
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello, World!", body);
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
