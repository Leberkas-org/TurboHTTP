using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests.RFC1945;

/// <summary>
/// Round-trip tests for HTTP/1.0 request methods per RFC 1945.
/// Verifies that GET, POST, and HEAD requests are correctly encoded and that the resulting responses are decoded.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="Http10EncoderStage"/> and <see cref="Http10DecoderStage"/>.
/// RFC 1945 §8: HTTP/1.0 request methods and their expected behaviour.
/// </remarks>
public sealed class Http10StageRoundTripMethodTests : StreamTestBase
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

    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return new DataItem(new SimpleMemoryOwner(bytes), bytes.Length) { Key = RequestEndpoint.Default };
    }

    private async Task<HttpResponseMessage> DecodeAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.1-10RT-001: GET → 200 OK — request-line + response correct")]
    public async Task Should_EncodeGetRequestAndDecode200Response_When_GetRequest()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.1-10RT-002: POST with body → body in wire format + 200 response")]
    public async Task Should_EncodePostBodyAndDecode200Response_When_PostWithBody()
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

    [Fact(Timeout = 10_000,
        DisplayName = "RFC1945-5.1-10RT-003: HEAD → response without body, but with Content-Length header")]
    public async Task Should_EncodeHeadRequestAndDecodeResponseWithoutBody_When_HeadRequest()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.1-10RT-004: DELETE → 204 No Content (empty body)")]
    public async Task Should_EncodeDeleteRequestAndDecode204Response_When_DeleteRequest()
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

    [Fact(Timeout = 10_000, DisplayName = "RFC1945-5.1-10RT-005: PUT → body correctly transmitted and response parsed")]
    public async Task Should_EncodeAndDecodeBodyCorrectly_When_PutWithBody()
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