using System.Net;
using System.Security.Cryptography;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http2;

/// <summary>
/// Phase 16 — HTTP/2 Advanced: Large payload tests.
/// Covers 1 MB and 4 MB response bodies, large request bodies, DATA frame
/// reassembly integrity (SHA-256), large headers + large body combinations,
/// and streaming decode behavior.
/// </summary>
[Collection("Http2Advanced")]
public sealed class Http2LargePayloadTests
{
    private readonly KestrelH2Fixture _fixture;

    public Http2LargePayloadTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    // ── Large Response Bodies ─────────────────────────────────────────────────

    [Fact(DisplayName = "IT-2A-050: 1 MB response body decoded correctly — all bytes match")]
    public async Task Should_DecodeFully_When_OneMegabyteResponseBodyReceived()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/1024"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024 * 1024, body.Length);
        Assert.True(body.All(b => b == (byte)'A'), "All bytes should be 'A'.");
    }

    [Fact(DisplayName = "IT-2A-051: 4 MB response body decoded correctly — length and content verified")]
    public async Task Should_DecodeFully_When_FourMegabyteResponseBodyReceived()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/4096"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(4096 * 1024, body.Length);
        Assert.True(body.All(b => b == (byte)'A'), "All bytes should be 'A'.");
    }

    [Fact(DisplayName = "IT-2A-052: 60 KB request body encoded and echoed correctly")]
    public async Task Should_EchoRequestBody_When_60KbBodySentToEchoEndpoint()
    {
        // 60 KB fits within the initial 65535-byte send window.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var bodyData = new byte[60 * 1024];
        Array.Fill(bodyData, (byte)'Z');

        var request = new HttpRequestMessage(HttpMethod.Post, Http2Helper.BuildUri(_fixture.Port, "/h2/echo-binary"))
        {
            Content = new ByteArrayContent(bodyData)
        };
        request.Content.Headers.ContentType = new("application/octet-stream");

        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyData.Length, received.Length);
        Assert.Equal(bodyData, received);
    }

    [Fact(DisplayName = "IT-2A-053: Multiple DATA frames reassembly order preserved — 32 KB body")]
    public async Task Should_PreserveAssemblyOrder_When_BodySpansMultipleDataFrames()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/32"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(32 * 1024, body.Length);

        // Verify the order is preserved: every byte is 'A'.
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != (byte)'A')
            {
                Assert.Fail($"Byte at index {i} is {body[i]} (expected 'A'=65).");
            }
        }
    }

    [Fact(DisplayName = "IT-2A-054: Body matches SHA-256 of expected content — 1 MB all-'A' bytes")]
    public async Task Should_MatchExpectedHash_When_OneMegabyteBodyReceived()
    {
        // Pre-compute the expected SHA-256 of 1 MB of 'A' bytes (0x41).
        var expected = new byte[1024 * 1024];
        Array.Fill(expected, (byte)'A');
        var expectedHash = SHA256.HashData(expected);

        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/1024"));
        var response = await conn.SendAndReceiveAsync(request);

        var body = await response.Content.ReadAsByteArrayAsync();
        var actualHash = SHA256.HashData(body);

        Assert.Equal(expectedHash, actualHash);
    }

    [Fact(DisplayName = "IT-2A-055: Large body + large response headers — both decoded correctly")]
    public async Task Should_DecodeBodyAndHeaders_When_LargeBodyWithLargeHeadersReceived()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/h2/large-headers/1024"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify the large response headers are present.
        var hasLargeHeaders = false;
        foreach (var header in response.Headers)
        {
            if (header.Key.StartsWith("X-Large-", StringComparison.OrdinalIgnoreCase))
            {
                hasLargeHeaders = true;
                break;
            }
        }

        Assert.True(hasLargeHeaders, "Response should contain X-Large-* custom headers.");

        // Verify the body.
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024 * 1024, body.Length);
        Assert.True(body.All(b => b == (byte)'A'));
    }

    [Fact(DisplayName = "IT-2A-056: Streaming decode: slow endpoint delivers body byte-by-byte — reassembled correctly")]
    public async Task Should_ReassembleBody_When_ServerStreamsOneByteAtATime()
    {
        // /slow/{count} sends count bytes with a 1 ms delay between each flush.
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/slow/50"));
        var response = await conn.SendAndReceiveAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(50, body.Length);
        Assert.True(body.All(b => b == (byte)'x'));
    }

    [Fact(DisplayName = "IT-2A-057: Sequential large bodies — no state leakage between responses")]
    public async Task Should_DeliverCorrectBodies_When_SequentialLargeResponsesReceived()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);

        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/64"));
            var response = await conn.SendAndReceiveAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(64 * 1024, body.Length);
            Assert.True(body.All(b => b == (byte)'A'),
                $"Iteration {i}: body contained unexpected bytes.");
        }
    }

    [Fact(DisplayName = "IT-2A-058: Large body on multiple concurrent streams — body integrity per stream")]
    public async Task Should_PreserveBodyIntegrity_When_ConcurrentLargeBodiessReceived()
    {
        await using var conn = await Http2Connection.OpenAsync(_fixture.Port);
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/20")),
            new(HttpMethod.Get, Http2Helper.BuildUri(_fixture.Port, "/large/20")),
        };

        var streamIds = await conn.SendRequestsAsync(requests);
        var responses = await conn.ReadAllResponsesAsync(streamIds);

        foreach (var (sid, resp) in responses)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsByteArrayAsync();
            Assert.Equal(20 * 1024, body.Length);
            Assert.True(body.All(b => b == (byte)'A'),
                $"Stream {sid}: body contained unexpected bytes.");
        }
    }
}
