using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests HTTP/2 header size limits enforced by the stream stage (security: DoS protection).
/// Verifies that oversized individual headers and total header blocks are rejected post-HPACK decompression.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20StreamStage"/>.
/// RFC 9113 §10.5.1: Limits on header field sizes after decompression prevent memory exhaustion.
/// </remarks>
public sealed class Http2DecoderHeaderLimitsTests : TestKit
{
    private readonly IMaterializer _materializer;
    private readonly HpackEncoder _encoder = new();

    public Http2DecoderHeaderLimitsTests()
        : base(ActorSystem.Create("h2-header-limits-" + Guid.NewGuid().ToString("N")[..8]))
    {
        _materializer = Sys.Materializer();
    }

    private ReadOnlyMemory<byte> EncodeHeaders(params (string Name, string Value)[] headers)
        => _encoder.Encode(headers);

    private async Task<IReadOnlyList<(HttpResponseMessage Response, int StreamId)>> RunStageAsync(
        Http20StreamStage stage,
        params Http2Frame[] frames)
    {
        return await Source.From(frames)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<(HttpResponseMessage Response, int StreamId)>(), _materializer);
    }

    private async Task<Http2Exception> RunStageExpectingExceptionAsync(
        Http20StreamStage stage,
        params Http2Frame[] frames)
    {
        var ex = await Assert.ThrowsAsync<Http2Exception>(async () =>
            await RunStageAsync(stage, frames));
        return ex;
    }

    // ── Default limits ────────────────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-001: Default MaxHeaderSize is 16KB")]
    public async Task Should_UseDefaultMaxHeaderSize_When_NoConfigProvided()
    {
        var stage = new Http20StreamStage();
        var value = new string('A', 16 * 1024 - 20);
        var hpack = EncodeHeaders((":status", "200"), ("x-big", value));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var results = await RunStageAsync(stage, frame);

        Assert.Single(results);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-002: Default MaxTotalHeaderSize is 64KB")]
    public async Task Should_UseDefaultMaxTotalHeaderSize_When_NoConfigProvided()
    {
        var stage = new Http20StreamStage();
        var headers = new List<(string, string)> { (":status", "200") };
        var headerValue = new string('B', 1000);
        for (var i = 0; i < 60; i++)
        {
            headers.Add(($"x-hdr-{i:d3}", headerValue));
        }
        var hpack = _encoder.Encode(headers);
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var results = await RunStageAsync(stage, frame);

        Assert.Single(results);
    }

    // ── Single header too large ───────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-003: Single header exceeding MaxHeaderSize rejected")]
    public async Task Should_ThrowHttp2Exception_When_SingleHeaderExceedsLimit()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 100);
        var bigValue = new string('X', 200);
        var hpack = EncodeHeaders((":status", "200"), ("x-big", bigValue));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);

        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Contains("x-big", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-004: Header exactly at MaxHeaderSize accepted")]
    public async Task Should_Accept_When_SingleHeaderExactlyAtLimit()
    {
        // "x" (1 byte) + value bytes = limit
        const int limit = 50;
        var value = new string('V', limit - 1);
        var stage = new Http20StreamStage(maxHeaderSize: limit);
        var hpack = EncodeHeaders((":status", "200"), ("x", value));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var results = await RunStageAsync(stage, frame);

        Assert.Single(results);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-005: Header one byte over MaxHeaderSize rejected")]
    public async Task Should_ThrowHttp2Exception_When_OneByteOverLimit()
    {
        const int limit = 50;
        var value = new string('V', limit); // "x" (1) + value (50) = 51 > 50
        var stage = new Http20StreamStage(maxHeaderSize: limit);
        var hpack = EncodeHeaders((":status", "200"), ("x", value));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-006: Multiple small headers within limit accepted")]
    public async Task Should_Accept_When_MultipleSmallHeadersWithinLimit()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 100);
        var hpack = EncodeHeaders((":status", "200"), ("x-a", "short"), ("x-b", "also-short"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var results = await RunStageAsync(stage, frame);

        Assert.Single(results);
    }

    // ── Total headers too large ───────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-007: Total headers exceeding MaxTotalHeaderSize rejected")]
    public async Task Should_ThrowHttp2Exception_When_TotalExceedsLimit()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 1000, maxTotalHeaderSize: 100);
        var headers = new List<(string, string)> { (":status", "200") };
        for (var i = 0; i < 10; i++)
        {
            headers.Add(($"x-hdr-{i:d2}", $"value-{i:d2}-padding"));
        }
        var hpack = _encoder.Encode(headers);
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);

        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Contains("100", ex.Message);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-008: Total headers exactly at limit accepted")]
    public async Task Should_Accept_When_TotalHeadersExactlyAtLimit()
    {
        // ":status" + "200" = 7 + 3 = 10 bytes; "x" + "v" = 1 + 1 = 2 bytes; total = 12
        var stage = new Http20StreamStage(maxHeaderSize: 100, maxTotalHeaderSize: 12);
        var hpack = EncodeHeaders((":status", "200"), ("x", "v"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var results = await RunStageAsync(stage, frame);

        Assert.Single(results);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-009: Total headers one byte over limit rejected")]
    public async Task Should_ThrowHttp2Exception_When_OneByteOverTotal()
    {
        // ":status" + "200" = 10 bytes; "x" + "vv" = 1 + 2 = 3 bytes; total = 13 > 12
        var stage = new Http20StreamStage(maxHeaderSize: 100, maxTotalHeaderSize: 12);
        var hpack = EncodeHeaders((":status", "200"), ("x", "vv"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    // ── CONTINUATION frame boundaries ────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-010: Headers split across CONTINUATION frames accepted when within limits")]
    public async Task Should_Accept_When_HeadersSplitAcrossContinuationFramesWithinLimits()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 500, maxTotalHeaderSize: 2000);
        var hpack = EncodeHeaders((":status", "200"), ("x-data", new string('Z', 100)));
        var hpackBytes = hpack.ToArray();

        // Split HPACK block across HEADERS + CONTINUATION
        var splitAt = hpackBytes.Length / 2;
        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: hpackBytes[..splitAt],
            endStream: false,
            endHeaders: false);
        var continuationFrame = new ContinuationFrame(
            streamId: 1,
            headerBlock: hpackBytes[splitAt..],
            endHeaders: true);
        // HandleContinuation always passes endStream=false to DecodeHeaders,
        // so we need a DATA frame with endStream=true to complete the stream.
        var dataFrame = new DataFrame(streamId: 1, data: ReadOnlyMemory<byte>.Empty, endStream: true);

        var results = await RunStageAsync(stage, headersFrame, continuationFrame, dataFrame);

        Assert.Single(results);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-011: Headers split across CONTINUATION rejected when exceeding limit")]
    public async Task Should_ThrowHttp2Exception_When_ContinuationHeadersExceedLimit()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 30);
        var bigValue = new string('Z', 100);
        var hpack = EncodeHeaders((":status", "200"), ("x-big", bigValue));
        var hpackBytes = hpack.ToArray();

        var splitAt = hpackBytes.Length / 2;
        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: hpackBytes[..splitAt],
            endStream: true,
            endHeaders: false);
        var continuationFrame = new ContinuationFrame(
            streamId: 1,
            headerBlock: hpackBytes[splitAt..],
            endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, headersFrame, continuationFrame);
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-012: Multiple CONTINUATION frames reassembled and validated")]
    public async Task Should_ValidateReassembledHeaders_When_MultipleContinuationFrames()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 500, maxTotalHeaderSize: 2000);
        var hpack = EncodeHeaders((":status", "200"), ("x-a", "val-a"), ("x-b", "val-b"));
        var hpackBytes = hpack.ToArray();

        // Split into 3 parts
        var split1 = hpackBytes.Length / 3;
        var split2 = 2 * hpackBytes.Length / 3;
        var headersFrame = new HeadersFrame(streamId: 1, headerBlock: hpackBytes[..split1], endStream: false, endHeaders: false);
        var cont1 = new ContinuationFrame(streamId: 1, headerBlock: hpackBytes[split1..split2], endHeaders: false);
        var cont2 = new ContinuationFrame(streamId: 1, headerBlock: hpackBytes[split2..], endHeaders: true);
        // HandleContinuation always passes endStream=false, so complete with empty DATA.
        var dataFrame = new DataFrame(streamId: 1, data: ReadOnlyMemory<byte>.Empty, endStream: true);

        var results = await RunStageAsync(stage, headersFrame, cont1, cont2, dataFrame);

        Assert.Single(results);
    }

    // ── Pseudo-headers counted ───────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-013: Pseudo-header :status counted toward total header size")]
    public async Task Should_CountPseudoHeader_When_CalculatingTotalHeaderSize()
    {
        // ":status" (7) + "200" (3) = 10 bytes; limit = 9 → should fail
        var stage = new Http20StreamStage(maxHeaderSize: 100, maxTotalHeaderSize: 9);
        var hpack = EncodeHeaders((":status", "200"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-014: Pseudo-header :status counted toward single header size")]
    public async Task Should_CountPseudoHeader_When_CalculatingSingleHeaderSize()
    {
        // ":status" (7) + "200" (3) = 10 bytes; maxHeaderSize = 9 → should fail
        var stage = new Http20StreamStage(maxHeaderSize: 9);
        var hpack = EncodeHeaders((":status", "200"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Contains(":status", ex.Message);
    }

    // ── Custom limits ─────────────────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-015: Custom MaxHeaderSize respected")]
    public async Task Should_RejectAtCustomLimit_When_MaxHeaderSizeOverridden()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 20);
        var hpack = EncodeHeaders((":status", "200"), ("x-too-long", "this-value-exceeds-limit"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-016: Custom MaxTotalHeaderSize respected")]
    public async Task Should_RejectAtCustomTotalLimit_When_MaxTotalHeaderSizeOverridden()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 500, maxTotalHeaderSize: 20);
        var hpack = EncodeHeaders((":status", "200"), ("x-a", "aaaaaa"), ("x-b", "bbbbbb"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    // ── Error message quality ────────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-017: Error message includes RFC section reference")]
    public async Task Should_IncludeRfcReference_When_HeaderTooLarge()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 20);
        var hpack = EncodeHeaders((":status", "200"), ("x-err", new string('E', 50)));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Contains("RFC 9113", ex.Message);
        Assert.Contains("10.5.1", ex.Message);
    }

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-018: Error message includes stream ID")]
    public async Task Should_IncludeStreamId_When_HeaderExceedsLimit()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 20);
        var hpack = EncodeHeaders((":status", "200"), ("x-err", new string('E', 50)));
        var frame = new HeadersFrame(streamId: 7, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);
        Assert.Contains("stream 7", ex.Message);
        Assert.Equal(7, ex.StreamId);
    }

    // ── Stream-scoped error ──────────────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-019: Header limit violations are stream-scoped errors")]
    public async Task Should_ProduceStreamScopedError_When_HeaderLimitViolated()
    {
        var stage = new Http20StreamStage(maxHeaderSize: 20);
        var hpack = EncodeHeaders((":status", "200"), ("x-big", new string('B', 50)));
        var frame = new HeadersFrame(streamId: 3, headerBlock: hpack, endStream: true, endHeaders: true);

        var ex = await RunStageExpectingExceptionAsync(stage, frame);

        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(3, ex.StreamId);
        Assert.False(ex.IsConnectionError);
    }

    // ── Parameterless backward compat ────────────────────────────────────────

    [Fact(Timeout = 5000, DisplayName = "RFC9113-10.5-HL-020: Parameterless construction still works")]
    public async Task Should_WorkWithDefaults_When_NoParametersProvided()
    {
        var stage = new Http20StreamStage();
        var hpack = EncodeHeaders((":status", "200"), ("content-type", "text/plain"));
        var frame = new HeadersFrame(streamId: 1, headerBlock: hpack, endStream: true, endHeaders: true);

        var results = await RunStageAsync(stage, frame);

        Assert.Single(results);
    }
}
