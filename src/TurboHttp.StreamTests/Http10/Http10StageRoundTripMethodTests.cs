using System.Buffers;
using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.Http10;

/// <summary>
/// RFC 1945 §8 — Round-trip method tests through Akka.Streams stages.
/// Each test encodes an HTTP/1.0 request via Http10EncoderStage, verifies the wire format,
/// then decodes a matching response via Http10DecoderStage and validates the result.
/// </summary>
public sealed class Http10StageRoundTripMethodTests : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<(IMemoryOwner<byte>, int)>(), Materializer);

        var sb = new StringBuilder();
        foreach (var (owner, length) in chunks)
        {
            sb.Append(Encoding.Latin1.GetString(owner.Memory.Span[..length]));
            owner.Dispose();
        }

        return sb.ToString();
    }

    private static (IMemoryOwner<byte>, int) Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return (new SimpleMemoryOwner(bytes), bytes.Length);
    }

    private async Task<HttpResponseMessage> DecodeAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-RT-M-001: GET → 200 OK — request-line + response correct")]
    public async Task ST_10RT_M_001_Get_200_Ok()
    {
        // Encode
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource")
        {
            Version = HttpVersion.Version10
        };
        var wire = await EncodeAsync(request);
        Assert.StartsWith("GET /resource HTTP/1.0\r\n", wire);

        // Decode response
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nOK");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("OK", body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-RT-M-002: POST with body → body in wire format + 200 response")]
    public async Task ST_10RT_M_002_Post_Body_200()
    {
        // Encode
        const string payload = "field=value&other=123";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        var wire = await EncodeAsync(request);

        Assert.StartsWith("POST /submit HTTP/1.0\r\n", wire);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(payload)}", wire);
        // Body follows after double-CRLF
        var separatorIdx = wire.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(separatorIdx >= 0, "Missing header/body separator");
        var bodyPart = wire[(separatorIdx + 4)..];
        Assert.Equal(payload, bodyPart);

        // Decode response
        const string responseBody = "{\"status\":\"created\"}";
        var response = await DecodeAsync(
            $"HTTP/1.0 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\n\r\n{responseBody}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var respBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(responseBody, respBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-RT-M-003: HEAD → response without body, but with Content-Length header")]
    public async Task ST_10RT_M_003_Head_No_Body_With_ContentLength()
    {
        // Encode
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource")
        {
            Version = HttpVersion.Version10
        };
        var wire = await EncodeAsync(request);
        Assert.StartsWith("HEAD /resource HTTP/1.0\r\n", wire);

        // HEAD response: has Content-Length header but no body
        // The server sends headers only; connection close signals end
        var response = await DecodeAsync("HTTP/1.0 200 OK\r\nContent-Length: 1024\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1024, response.Content.Headers.ContentLength);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-RT-M-004: DELETE → 204 No Content (empty body)")]
    public async Task ST_10RT_M_004_Delete_204_NoContent()
    {
        // Encode
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/42")
        {
            Version = HttpVersion.Version10
        };
        var wire = await EncodeAsync(request);
        Assert.StartsWith("DELETE /resource/42 HTTP/1.0\r\n", wire);

        // Decode 204 response (no body)
        var response = await DecodeAsync("HTTP/1.0 204 No Content\r\n\r\n");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC-1945-§8-RT-M-005: PUT → body correctly transmitted and response parsed")]
    public async Task ST_10RT_M_005_Put_Body_RoundTrip()
    {
        // Encode
        const string payload = "{\"name\":\"updated\"}";
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource/7")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var wire = await EncodeAsync(request);

        Assert.StartsWith("PUT /resource/7 HTTP/1.0\r\n", wire);
        Assert.Contains("Content-Type: application/json", wire);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(payload)}", wire);
        var separatorIdx = wire.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var bodyPart = wire[(separatorIdx + 4)..];
        Assert.Equal(payload, bodyPart);

        // Decode response
        const string responseBody = "{\"id\":7,\"name\":\"updated\"}";
        var response = await DecodeAsync(
            $"HTTP/1.0 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\n\r\n{responseBody}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var respBody = await response.Content.ReadAsStringAsync();
        Assert.Equal(responseBody, respBody);
    }
}
