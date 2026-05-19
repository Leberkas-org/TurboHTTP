using TurboHTTP.Client;
using System.Net;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Stages;

public sealed class Http11KeepAliveCloseSpec : EngineTestBase
{
    private static Http11Engine Engine => new(new TurboClientOptions());

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.6")]
    public async Task Http11KeepAliveClose_should_set_version_when_connection_close_header_present()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/close");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
        Assert.Contains("close", response.Headers.Connection);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", body);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.8")]
    public async Task Http11KeepAliveClose_should_default_to_keep_alive_when_no_connection_header()
    {
        var requests = Enumerable.Range(1, 3)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/item/{i}"))
            .ToArray();

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests,
            () => "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"u8.ToArray(), 3);

        Assert.Equal(3, responses.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, responses[i].StatusCode);
            Assert.Equal(new Version(1, 1), responses[i].Version);
            Assert.Empty(responses[i].Headers.Connection);
        }
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.8")]
    public async Task Http11KeepAliveClose_should_keep_stream_open_when_chunked_with_keep_alive()
    {
        var requests = Enumerable.Range(1, 2)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/chunked/{i}"))
            .ToArray();

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests,
            () => ("HTTP/1.1 200 OK\r\n" +
                   "Transfer-Encoding: chunked\r\n" +
                   "Connection: keep-alive\r\n" +
                   "\r\n" +
                   "5\r\nhello\r\n0\r\n\r\n").Select(c => (byte)c).ToArray(), 2);

        Assert.Equal(2, responses.Count);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.OK, responses[i].StatusCode);
            Assert.Contains("keep-alive", responses[i].Headers.Connection);
            var body = await responses[i].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("hello", body);
        }
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.6")]
    public async Task Http11KeepAliveClose_should_read_content_length_body_when_connection_not_prematurely_closed()
    {
        var requests = Enumerable.Range(1, 3)
            .Select(i => new HttpRequestMessage(HttpMethod.Get, $"http://example.com/data/{i}"))
            .ToArray();

        const string bodyText = "The quick brown fox jumps over the lazy dog.";
        var bodyBytes = System.Text.Encoding.ASCII.GetBytes(bodyText);

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests,
            () => System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {bodyBytes.Length}\r\n\r\n{bodyText}"), 3);

        Assert.Equal(3, responses.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, responses[i].StatusCode);
            var body = await responses[i].Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal(bodyText, body);
            Assert.Equal(bodyBytes.Length, responses[i].Content.Headers.ContentLength);
        }
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9112-9.6")]
    public async Task Http11KeepAliveClose_should_emit_response_immediately_when_content_length_is_zero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/empty");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(body);
        Assert.NotNull(response.RequestMessage);
        Assert.Same(request, response.RequestMessage);
    }
}