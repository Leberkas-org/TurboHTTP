using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Phase 14 — HTTP/1.1 Integration Tests: Range requests (RFC 7233).
/// Tests cover byte ranges, suffix ranges, open-ended ranges, unsatisfiable ranges,
/// If-Range conditional, and multi-range requests.
/// </summary>
[Collection("Http11Integration")]
public sealed class Http11RangeTests
{
    private readonly KestrelFixture _fixture;

    public Http11RangeTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── No Range header → 200 full body ──────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-016: No Range header — GET /range/1 returns 200 with full 1 KB body")]
    public async Task NoRangeHeader_Returns200_FullBody()
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, "/range/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024, body.Length);
    }

    // ── Range: bytes=0-99 → 206, Content-Range ───────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-017: Range: bytes=0-99 — returns 206 with 100 bytes and Content-Range header")]
    public async Task Range_Bytes0To99_Returns206_ContentRange()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-99");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(100, body.Length);
        // Content-Range header should be present
        Assert.True(response.Content.Headers.ContentRange != null,
            "Content-Range header should be present in 206 response");
        Assert.Equal(0, response.Content.Headers.ContentRange!.From);
        Assert.Equal(99, response.Content.Headers.ContentRange.To);
    }

    // ── Range: bytes=0-0 → 1 byte ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-018: Range: bytes=0-0 — returns 206 with exactly 1 byte")]
    public async Task Range_Bytes0To0_Returns206_OneByte()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-0");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Single(body);
        // First byte of sequential data is 0 % 256 = 0
        Assert.Equal(0, body[0]);
    }

    // ── Range: bytes=-100 → last 100 bytes ───────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-019: Range: bytes=-100 — returns 206 with last 100 bytes")]
    public async Task Range_SuffixRange100_Returns206_Last100Bytes()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=-100");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(100, body.Length);
    }

    // ── Range: bytes=100- → from byte 100 to end ─────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-020: Range: bytes=100- — returns 206 from byte 100 to end of 1 KB body")]
    public async Task Range_OpenEndedFrom100_Returns206_RestOfBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=100-");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        // 1 KB = 1024 bytes; starting from byte 100 = 924 bytes
        Assert.Equal(924, body.Length);
        // Verify first byte of range is correct sequential value (100 % 256 = 100)
        Assert.Equal(100, body[0]);
    }

    // ── Range on 1 KB body ────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-021: Range: bytes=512-1023 on 1 KB body — returns 206 with second half")]
    public async Task Range_SecondHalf_1KbBody_Returns206()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=512-1023");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(512, body.Length);
        // First byte at offset 512 = 512 % 256 = 0
        Assert.Equal(0, body[0]);
    }

    // ── Range on 64 KB body ───────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-022: Range: bytes=0-4095 on 64 KB body — returns 206 with first 4 KB")]
    public async Task Range_First4KB_64KbBody_Returns206()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/64"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-4095");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(4096, body.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-023: Range: bytes=32768-65535 on 64 KB body — returns 206 with second half")]
    public async Task Range_SecondHalf_64KbBody_Returns206()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/64"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=32768-65535");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(32768, body.Length);
    }

    // ── Range: unsatisfiable → 416 ────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-024: Range: bytes=99999-99999 on 1 KB body — returns 416 Range Not Satisfiable")]
    public async Task Range_Unsatisfiable_Returns416()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=99999-99999");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
    }

    // ── If-Range with matching ETag ───────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-025: If-Range with matching ETag — returns 206 partial content")]
    public async Task IfRange_MatchingETag_Returns206()
    {
        // First, get the ETag from the resource
        var etag = await GetEtagAsync("/range/etag");

        // Now request with If-Range matching that ETag
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/etag"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-99");
        request.Headers.TryAddWithoutValidation("If-Range", etag);

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(100, body.Length);
    }

    // ── If-Range with non-matching ETag → 200 full body ──────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-026: If-Range with non-matching ETag — returns 200 with full body")]
    public async Task IfRange_NonMatchingETag_Returns200_FullBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/etag"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-99");
        request.Headers.TryAddWithoutValidation("If-Range", "\"stale-etag\"");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        // Non-matching ETag: server ignores Range, returns 200 with full body
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(512, body.Length);
    }

    // ── Range: bytes=0-49,50-99 (multi-range) ────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-027: Range: bytes=0-49,50-99 (multi-range) — server returns 200 or 206")]
    public async Task Range_MultiRange_Returns200Or206()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-49,50-99");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        // ASP.NET Core may return 206 (multipart/byteranges) or 200 for multi-range
        Assert.True(
            response.StatusCode == HttpStatusCode.PartialContent ||
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 or 206, got {(int)response.StatusCode}");
    }

    // ── Content-Range header structure ───────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-028: 206 response includes Content-Range with total resource size")]
    public async Task Range_206Response_ContentRange_IncludesTotalSize()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-99");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var cr = response.Content.Headers.ContentRange;
        Assert.NotNull(cr);
        // The length (total resource size) should be 1024
        Assert.Equal(1024L, cr!.Length);
        Assert.Equal("bytes", cr.Unit);
    }

    // ── Range preserves body content ─────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "IT-11A-029: Range bytes=256-511 on 1 KB body — body bytes match sequential pattern")]
    public async Task Range_BodyBytes_MatchSequentialPattern()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/range/1"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=256-511");

        var response = await Http11Helper.SendAsync(_fixture.Port, request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(256, body.Length);
        // Check sequential pattern: body[i] = (256 + i) % 256
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)((256 + i) % 256), body[i]);
        }
    }

    // ── Helper: retrieve ETag from a resource ─────────────────────────────────

    private async Task<string> GetEtagAsync(string path)
    {
        var response = await Http11Helper.GetAsync(_fixture.Port, path);
        var etag = response.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrEmpty(etag), $"Resource {path} should have an ETag");
        return etag!;
    }
}
