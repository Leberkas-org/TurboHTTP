using System.Net;
using System.Text;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Decoding;
using TurboHTTP.Streams.Stages.Encoding;

namespace TurboHTTP.StreamTests.Http10;

/// <summary>
/// Round-trip tests for HTTP/1.0 request methods per RFC 1945.
/// Verifies that GET, POST, and HEAD requests are correctly encoded and that the resulting responses are decoded.
/// </summary>
/// <remarks>
/// Stages under test: <see cref="Http10EncoderStage"/> and <see cref="Http10DecoderStage"/>.
/// RFC 1945 §8: HTTP/1.0 request methods and their expected behaviour.
/// </remarks>
public sealed class Http10RoundTripMethodSpec : StreamTestBase
{
    private async Task<string> EncodeAsync(HttpRequestMessage request)
    {
        var chunks = await Source.Single(request)
            .Via(Flow.FromGraph(new Http10EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        var sb = new StringBuilder();
        foreach (var item in chunks)
        {
            var data = (NetworkBuffer)item;
            sb.Append(Encoding.Latin1.GetString(data.Span));
            data.Dispose();
        }

        return sb.ToString();
    }

    private static IInputItem Chunk(string ascii)
    {
        var bytes = Encoding.Latin1.GetBytes(ascii);
        return NetworkBuffer.FromArray(bytes);
    }

    private async Task<HttpResponseMessage> DecodeAsync(params string[] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http10DecoderStage()))
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10RoundTripMethod_should_encode_get_request_and_decode_200_response_when_get_request()
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
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("OK", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10RoundTripMethod_should_encode_post_body_and_decode_200_response_when_post_with_body()
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
        var respBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(responseBody, respBody);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10RoundTripMethod_should_encode_head_request_and_decode_response_without_body_when_head_request()
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
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10RoundTripMethod_should_encode_delete_request_and_decode_204_response_when_delete_request()
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
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-5.1")]
    public async Task Http10RoundTripMethod_should_encode_and_decode_body_correctly_when_put_with_body()
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
        var respBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(responseBody, respBody);
    }
}
