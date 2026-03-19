using System.Net;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Http11;

/// <summary>
/// RFC 9112 §9.6/§9.8 — HTTP/1.1 Connection Management round-trip tests through Akka.Streams stages.
/// Validates Connection: close header handling, default keep-alive behavior, chunked transfer
/// with keep-alive, Content-Length body reading, and empty body emission.
/// </summary>
public sealed class Http11StageConnectionMgmtTests : EngineTestBase
{
    private static Http11Engine Engine => new();

    // ── 11RT-C-001: Response with Connection: close → version correctly set ──────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.6-11CM-001: Response with Connection: close → version correctly set")]
    public async Task _11RT_C_001_Connection_Close_Version_Set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/close");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);
        Assert.Contains("close", response.Headers.Connection);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    // ── 11RT-C-002: Response without Connection header → keep-alive (default) ────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.8-11CM-002: Response without Connection header → keep-alive default")]
    public async Task _11RT_C_002_No_Connection_Header_KeepAlive_Default()
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

    // ── 11RT-C-003: Chunked + Connection: keep-alive → stream stays open ─────────

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.8-11CM-003: Chunked + Connection: keep-alive → stream stays open")]
    public async Task _11RT_C_003_Chunked_KeepAlive_Stream_Stays_Open()
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
            var body = await responses[i].Content.ReadAsStringAsync();
            Assert.Equal("hello", body);
        }
    }

    // ── 11RT-C-004: Content-Length body → correctly read, not prematurely closed ──

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.6-11CM-004: Content-Length body → correctly read, connection not prematurely closed")]
    public async Task _11RT_C_004_ContentLength_Body_Correctly_Read()
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
            var body = await responses[i].Content.ReadAsStringAsync();
            Assert.Equal(bodyText, body);
            Assert.Equal(bodyBytes.Length, responses[i].Content.Headers.ContentLength);
        }
    }

    // ── 11RT-C-005: Empty body with Content-Length: 0 → response emitted immediately

    [Fact(Timeout = 10_000, DisplayName = "RFC9112-9.6-11CM-005: Empty body with Content-Length: 0 → response emitted immediately")]
    public async Task _11RT_C_005_Empty_Body_ContentLength_Zero_Immediate()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/empty");

        var (response, _) = await SendAsync(Engine.CreateFlow(), request,
            () => "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Version(1, 1), response.Version);

        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
        Assert.NotNull(response.RequestMessage);
        Assert.Same(request, response.RequestMessage);
    }
}
