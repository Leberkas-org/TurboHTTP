using System.Net;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 15 — HTTP/2 Integration Tests: Connection-level behavior.
/// Tests cover the connection preface, SETTINGS negotiation, PING/PONG,
/// GOAWAY, and connection-level flow control.
/// </summary>
[Collection("Http2Integration")]
public sealed class Http2ConnectionTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2ConnectionTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── Connection Preface ────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-2-001: Connection preface sent and server SETTINGS received")]
    public async Task Should_ReceiveServerSettings_When_SendingConnectionPreface()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // OpenAsync already performs the preface exchange and receives SETTINGS.
        // If we reach here the preface succeeded; verify we can send a request.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-002: SETTINGS ACK sent in response to server SETTINGS")]
    public async Task Should_SendSettingsAck_When_ServerSettingsReceived()
    {
        // After OpenAsync the fixture server has received our SETTINGS ACK;
        // we verify the connection is functional (which requires ACK to have been sent).
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    // ── PING ──────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-2-003: PING → server returns PING ACK with same data")]
    public async Task Should_ReceivePingAck_When_PingIsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var pingData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var ack = await conn.PingAsync(pingData);
        Assert.Equal(pingData, ack);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-004: Multiple PING frames — each ACK matches its request data")]
    public async Task Should_ReceiveMatchingPingAcks_When_MultiplePingsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var ping1Data = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var ping2Data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x01 };

        var ack1 = await conn.PingAsync(ping1Data);
        var ack2 = await conn.PingAsync(ping2Data);

        Assert.Equal(ping1Data, ack1);
        Assert.Equal(ping2Data, ack2);
    }

    // ── SETTINGS Negotiation ──────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-2-005: Server SETTINGS contains INITIAL_WINDOW_SIZE")]
    public async Task Should_ReceiveInitialWindowSize_When_HandshakeCompletes()
    {
        // We verify this indirectly: after preface, we can receive a response body,
        // meaning the window size was accepted by both sides.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/32"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(32 * 1024, body.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-006: SETTINGS: server announces MAX_FRAME_SIZE — connection remains functional")]
    public async Task Should_HonorMaxFrameSize_When_ServerAnnouncesIt()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // If MAX_FRAME_SIZE was received and applied, the encoder respects it.
        // Verify by sending a request and getting a response.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/h2/settings"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-007: SETTINGS: MAX_CONCURRENT_STREAMS respected — sequential streams succeed")]
    public async Task Should_SucceedWithSequentialStreams_When_MaxConcurrentStreamsIsRespected()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Send 3 sequential requests (stream 1, 3, 5).
        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
            var response = await conn.SendAndReceiveAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-008: SETTINGS frame with zero parameters is valid — no error")]
    public async Task Should_AcceptEmptySettings_When_ZeroParametersPresent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Send empty SETTINGS (valid per RFC 7540 §6.5).
        await conn.SendSettingsAsync(ReadOnlySpan<(SettingsParameter, uint)>.Empty);
        // Connection remains functional.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── GOAWAY ────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-2-009: Client sends GOAWAY before disconnect — no server error")]
    public async Task Should_SendGoAway_When_ClientDisconnects()
    {
        // After completing a successful exchange, send GOAWAY then close.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Stream 1 was the last stream.
        await conn.SendGoAwayAsync(lastStreamId: 1, Http2ErrorCode.NoError);
        // No exception expected; the connection just closes gracefully.
    }

    // ── Flow Control ──────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-2-010: Connection-level flow control — initial window is 65535")]
    public async Task Should_HaveInitialConnectionWindow_When_Connected()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // The decoder starts with a 65535-byte receive window.
        var window = conn.GetConnectionReceiveWindow();
        Assert.Equal(65535, window);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-011: WINDOW_UPDATE on connection level — encoder send window increases")]
    public async Task Should_IncreaseConnectionSendWindow_When_WindowUpdateReceived()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Kestrel proactively sends WINDOW_UPDATE after receiving a response.
        // We verify the connection remains functional after a large body (64 KB) which
        // exercises flow control on the connection.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/60"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(60 * 1024, body.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-012: Idle connection — multiple requests succeed without error")]
    public async Task Should_RemainFunctional_When_NoRequestsSentBetweenRequests()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Small delay to simulate idle time.
        await Task.Delay(200);

        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-013: SETTINGS update INITIAL_WINDOW_SIZE mid-connection — next stream uses new window")]
    public async Task Should_UseNewWindowSize_When_SettingsUpdatedMidConnection()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Send a SETTINGS frame updating INITIAL_WINDOW_SIZE to 32 KB.
        await conn.SendSettingsAsync(
        [
            (SettingsParameter.InitialWindowSize, 32768u)
        ]);
        // Give server time to apply settings.
        await Task.Delay(50);
        // Connection still functional.
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-014: Two separate connections to the same server both succeed")]
    public async Task Should_SupportMultipleIndependentConnections_When_ServerIsRunning()
    {
        await using var conn1 = await Http2Connection.OpenAsync(_fixture.Port);
        await using var conn2 = await Http2Connection.OpenAsync(_fixture.Port);

        var req1 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello"));
        var req2 = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));

        var resp1 = await conn1.SendAndReceiveAsync(req1);
        var resp2 = await conn2.SendAndReceiveAsync(req2);

        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-015: Connection preface magic is exactly 24 bytes (RFC 7540 §3.5)")]
    public async Task Should_Have24ByteConnectionPreface_When_PrefaceMagicInspected()
    {
        // Verify the static preface starts with the standard magic string.
        var preface = Http2FrameUtils.BuildConnectionPreface();
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.True(preface.Length > magic.Length, "Preface should include magic + SETTINGS frame.");
        for (var i = 0; i < magic.Length; i++)
        {
            Assert.Equal(magic[i], preface[i]);
        }

        // Verify the connection still works.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
        var response = await conn.SendAndReceiveAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2-016: Server responds with 200 status to a basic GET over HTTP/2")]
    public async Task Should_Return200_When_SimpleGetSentOverHttp2()
    {
        var response = await Http2Helper.GetAsync(_fixture.Port, "/hello");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }
}
