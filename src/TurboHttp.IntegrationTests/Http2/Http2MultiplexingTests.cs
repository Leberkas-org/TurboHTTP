using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 16 — HTTP/2 Advanced: Stream multiplexing tests.
/// Covers concurrent streams, interleaved DATA frames, MAX_CONCURRENT_STREAMS
/// negotiation, and stream reuse patterns.
/// </summary>
[Collection("Http2")]
public sealed class Http2MultiplexingTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2MultiplexingTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── Concurrent Streams ────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-001: 2 concurrent streams on same connection — both return 200")]
    public async Task Should_ReturnBothResponses_When_TwoConcurrentStreamsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello")),
        };

        var responses = await conn.SendAndReceiveMultipleAsync(requests);

        Assert.Equal(2, responses.Count);
        foreach (var (_, resp) in responses)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-002: 4 concurrent streams on same connection — all return 200")]
    public async Task Should_ReturnAllResponses_When_FourConcurrentStreamsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = Enumerable.Range(0, 4)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")))
            .ToList();

        var responses = await conn.SendAndReceiveMultipleAsync(requests);

        Assert.Equal(4, responses.Count);
        Assert.True(responses.Values.All(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-003: 8 concurrent streams on same connection — all return 200")]
    public async Task Should_ReturnAllResponses_When_EightConcurrentStreamsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = Enumerable.Range(0, 8)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")))
            .ToList();

        var responses = await conn.SendAndReceiveMultipleAsync(requests);

        Assert.Equal(8, responses.Count);
        Assert.True(responses.Values.All(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-004: 16 concurrent streams on same connection — all return 200")]
    public async Task Should_ReturnAllResponses_When_SixteenConcurrentStreamsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = Enumerable.Range(0, 16)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")))
            .ToList();

        var responses = await conn.SendAndReceiveMultipleAsync(requests);

        Assert.Equal(16, responses.Count);
        Assert.True(responses.Values.All(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-005: Streams interleaved: large + small body concurrently — both correct")]
    public async Task Should_ReassembleBothBodies_When_DataFramesInterleaved()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/20")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")),
        };

        var responses = await conn.SendAndReceiveMultipleAsync(requests);

        Assert.Equal(2, responses.Count);
        var streamIds = responses.Keys.OrderBy(x => x).ToList();

        var largeBody = await responses[streamIds[0]].Content.ReadAsByteArrayAsync();
        var smallBody = await responses[streamIds[1]].Content.ReadAsByteArrayAsync();

        // Stream IDs are 1 and 3; stream 1 = /large/20, stream 3 = /ping
        Assert.Equal(20 * 1024, largeBody.Length);
        Assert.Equal("pong", Encoding.UTF8.GetString(smallBody));
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-006: Streams complete out of order — collector handles any arrival order")]
    public async Task Should_CollectAllResponses_When_StreamsCompleteOutOfOrder()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Large body on stream 1 (completes later), fast response on stream 3
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/30")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")),
        };

        var streamIds = await conn.SendRequestsAsync(requests);
        var responses = await conn.ReadAllResponsesAsync(streamIds);

        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[streamIds[0]].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[streamIds[1]].StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-007: High-priority (small) + low-priority (large) streams — both complete correctly")]
    public async Task Should_CompleteBothStreams_When_DifferentBodySizesUsed()
    {
        // HTTP/2 priority is deprecated (RFC 9113), but both streams must complete.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/h2/priority/32")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")),
        };

        var responses = await conn.SendAndReceiveMultipleAsync(requests);

        Assert.Equal(2, responses.Count);
        Assert.True(responses.Values.All(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-008: Concurrent GET + POST — both return correct responses")]
    public async Task Should_ReturnCorrectResponses_When_ConcurrentGetAndPostSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello")),
            new(HttpMethod.Post, Http2Helper.BuildUri(_fixture.Port, "/echo"))
            {
                Content = new StringContent("concurrent-post", Encoding.UTF8, "text/plain")
            },
        };

        var responses = await conn.SendAndReceiveMultipleAsync(requests);

        Assert.Equal(2, responses.Count);
        var streamIds = responses.Keys.OrderBy(x => x).ToList();

        Assert.Equal(HttpStatusCode.OK, responses[streamIds[0]].StatusCode);
        Assert.Equal("Hello World", await responses[streamIds[0]].Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, responses[streamIds[1]].StatusCode);
        var echoBody = await responses[streamIds[1]].Content.ReadAsStringAsync();
        Assert.Contains("concurrent-post", echoBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-009: Stream 1 large body + stream 3 small body — body integrity preserved")]
    public async Task Should_PreserveBodyIntegrity_When_LargeAndSmallBodiesInterleaved()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/32")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")),
        };

        var streamIds = await conn.SendRequestsAsync(requests);
        var responses = await conn.ReadAllResponsesAsync(streamIds);

        var largeBody = await responses[streamIds[0]].Content.ReadAsByteArrayAsync();
        Assert.Equal(32 * 1024, largeBody.Length);
        Assert.True(largeBody.All(b => b == (byte)'A'));

        var smallBody = await responses[streamIds[1]].Content.ReadAsStringAsync();
        Assert.Equal("pong", smallBody);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-010: MAX_CONCURRENT_STREAMS = 1: sequential requests succeed after SETTINGS")]
    public async Task Should_SucceedSequentially_When_MaxConcurrentStreamsIsOne()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        // Send SETTINGS with MAX_CONCURRENT_STREAMS = 1 (telling the server our limit).
        await conn.SendSettingsAsync([(SettingsParameter.MaxConcurrentStreams, 1u)]);
        await Task.Delay(50);

        // Sequential requests should still succeed.
        for (var i = 0; i < 3; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
            var resp = await conn.SendAndReceiveAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-011: MAX_CONCURRENT_STREAMS = 4: five sequential requests all succeed")]
    public async Task Should_SucceedAllRequests_When_MaxConcurrentStreamsFourAndFiveRequestsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        await conn.SendSettingsAsync([(SettingsParameter.MaxConcurrentStreams, 4u)]);
        await Task.Delay(50);

        // Five sequential requests — all should succeed.
        for (var i = 0; i < 5; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
            var resp = await conn.SendAndReceiveAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-012: All concurrent streams return correct bodies — each verified individually")]
    public async Task Should_ReturnCorrectBodyPerStream_When_FourConcurrentRequestsSent()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello")),
        };

        var streamIds = await conn.SendRequestsAsync(requests);
        var responses = await conn.ReadAllResponsesAsync(streamIds);

        var body1 = await responses[streamIds[0]].Content.ReadAsStringAsync();
        var body2 = await responses[streamIds[1]].Content.ReadAsStringAsync();
        var body3 = await responses[streamIds[2]].Content.ReadAsStringAsync();
        var body4 = await responses[streamIds[3]].Content.ReadAsStringAsync();

        Assert.Equal("pong", body1);
        Assert.Equal("Hello World", body2);
        Assert.Equal("pong", body3);
        Assert.Equal("Hello World", body4);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-013: Concurrent streams with different response codes — all decoded correctly")]
    public async Task Should_DecodeCorrectStatusCodes_When_ConcurrentStreamsReturnDifferentCodes()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/status/200")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/status/404")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/status/500")),
        };

        var streamIds = await conn.SendRequestsAsync(requests);
        var responses = await conn.ReadAllResponsesAsync(streamIds);

        Assert.Equal(HttpStatusCode.OK, responses[streamIds[0]].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, responses[streamIds[1]].StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, responses[streamIds[2]].StatusCode);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-014: Two streams with same request path — both return identical bodies")]
    public async Task Should_ReturnSameBody_When_TwoStreamsRequestSamePath()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/hello")),
        };

        var streamIds = await conn.SendRequestsAsync(requests);
        var responses = await conn.ReadAllResponsesAsync(streamIds);

        var body1 = await responses[streamIds[0]].Content.ReadAsStringAsync();
        var body2 = await responses[streamIds[1]].Content.ReadAsStringAsync();

        Assert.Equal("Hello World", body1);
        Assert.Equal("Hello World", body2);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-2A-015: Stream reuse: 20 sequential streams on one connection — all succeed")]
    public async Task Should_SucceedAllStreams_When_TwentySequentialStreamsUsed()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        for (var i = 0; i < 20; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/ping"));
            var resp = await conn.SendAndReceiveAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("pong", await resp.Content.ReadAsStringAsync());
        }
    }
}
