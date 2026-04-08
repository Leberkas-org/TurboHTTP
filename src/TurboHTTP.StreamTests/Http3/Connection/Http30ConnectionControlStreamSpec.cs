using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Streams.Stages.Decoding;

namespace TurboHTTP.StreamTests.Http3.Connection;

/// <summary>
/// Stage-behaviour tests for SETTINGS exchange on the HTTP/3 control stream.
/// Pushes SETTINGS frames into <see cref="Http30ConnectionStage"/> via InServer
/// and verifies observable output on OutApp / OutServer / stage completion.
/// </summary>
/// <remarks>
/// Replaces deleted Protocol Layer tests: 11_ControlStreamSpec.cs, 24_SettingsExchangeSpec.cs.
/// RFC 9114 Section 6.2.1: Control stream — first frame MUST be SETTINGS.
/// RFC 9114 Section 7.2.4: SETTINGS frame processing and validation.
/// RFC 9114 Section 7.2.4.1: Reserved HTTP/2 settings MUST cause SettingsError.
/// </remarks>
public sealed class Http30ConnectionControlStreamSpec : StreamTestBase
{
    private async Task<(IReadOnlyList<Http3Frame> Downstream, IReadOnlyList<Http3Frame> ServerBound)> RunAsync(
        params Http3Frame[] serverFrames)
    {
        var downstreamSink = Sink.Seq<Http3Frame>();
        var serverBoundSink = Sink.Seq<Http3Frame>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(downstreamSink, serverBoundSink,
                (m1, m2) => (m1, m2),
                (b, dsSink, sbSink) =>
                {
                    var stage = b.Add(new Http30ConnectionStage());
                    var serverSource = b.Add(Source.From(serverFrames));
                    var requestSource = b.Add(Source.Never<Http3Frame>());

                    b.From(serverSource).To(stage.InServer);
                    b.From(stage.OutApp).To(dsSink);
                    b.From(requestSource).To(stage.InApp);
                    b.From(stage.OutServer).To(sbSink);

                    return ClosedShape.Instance;
                }));

        var (downstreamTask, serverBoundTask) = graph.Run(Materializer);

        var downstream = await downstreamTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverBound = await serverBoundTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        return (downstream, serverBound);
    }

    // --- SETTINGS absorption (RFC 9114 Section 7.2.4) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30ConnectionControlStream_should_absorb_settings_with_parameters_when_received()
    {
        var settings = new Http3SettingsFrame([
            (Http3SettingsIdentifier.MaxFieldSectionSize, 8192),
            (Http3SettingsIdentifier.QpackMaxTableCapacity, 4096),
            (Http3SettingsIdentifier.QpackBlockedStreams, 100)
        ]);

        var (downstream, _) = await RunAsync(settings);

        // SETTINGS is connection-level — never forwarded to the app
        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30ConnectionControlStream_should_continue_routing_frames_after_settings_received()
    {
        var settings = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 8192)]);
        var data = new Http3DataFrame(new byte[] { 0x01, 0x02 });

        var (downstream, _) = await RunAsync(settings, data);

        // DATA should pass through after SETTINGS is absorbed
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    // --- Duplicate SETTINGS (RFC 9114 Section 7.2.4) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30ConnectionControlStream_should_absorb_duplicate_settings_without_failing_stage()
    {
        var settings1 = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 8192)]);
        var settings2 = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 4096)]);
        var data = new Http3DataFrame(new byte[] { 0xAA });

        var (downstream, _) = await RunAsync(settings1, settings2, data);

        // Duplicate SETTINGS is a FrameUnexpected error per RFC 9114 Section 7.2.4,
        // but the stage absorbs it (logs warning) and continues routing.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    // --- Reserved HTTP/2 settings (RFC 9114 Section 7.2.4.1) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    public async Task Http30ConnectionControlStream_should_absorb_reserved_h2_setting_without_failing_stage()
    {
        // ENABLE_PUSH (0x02) is a reserved HTTP/2 setting forbidden in HTTP/3.
        // Http3Settings.Set() throws SettingsError, which HandleSettings absorbs.
        var settings = new Http3SettingsFrame([(0x02, 1)]);
        var data = new Http3DataFrame(new byte[] { 0xBB });

        var (downstream, _) = await RunAsync(settings, data);

        // The error is absorbed — stage continues routing subsequent frames.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    [Theory(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    [InlineData(0x02)] // ENABLE_PUSH
    [InlineData(0x03)] // MAX_CONCURRENT_STREAMS
    [InlineData(0x04)] // INITIAL_WINDOW_SIZE
    [InlineData(0x05)] // MAX_FRAME_SIZE
    public async Task Http30ConnectionControlStream_should_absorb_any_reserved_h2_setting_when_received(long reservedId)
    {
        var settings = new Http3SettingsFrame([(reservedId, 42)]);

        // Stage should not fail — the error is absorbed.
        var (downstream, _) = await RunAsync(settings);

        Assert.Empty(downstream);
    }

    // --- Unknown extension settings (RFC 9114 Section 7.2.4) ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30ConnectionControlStream_should_absorb_unknown_extension_settings_when_received()
    {
        var settings = new Http3SettingsFrame([
            (0x33, 999),
            (0xFF, 42)
        ]);
        var data = new Http3DataFrame(new byte[] { 0xCC });

        var (downstream, _) = await RunAsync(settings, data);

        // Unknown settings are accepted per RFC 9114 Section 7.2.4.
        Assert.Single(downstream);
        Assert.IsType<Http3DataFrame>(downstream[0]);
    }

    // --- SETTINGS followed by control-stream frames ---

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30ConnectionControlStream_should_absorb_settings_then_goaway_when_both_received()
    {
        var settings = new Http3SettingsFrame([]);
        var goaway = new Http3GoAwayFrame(0);

        var (downstream, _) = await RunAsync(settings, goaway);

        // Both are connection-level control frames — nothing forwarded
        Assert.Empty(downstream);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public async Task Http30ConnectionControlStream_should_absorb_settings_then_forward_headers_when_received()
    {
        var settings = new Http3SettingsFrame([(Http3SettingsIdentifier.MaxFieldSectionSize, 16384)]);
        var headers = new Http3HeadersFrame(new byte[] { 0x00, 0x00 });

        var (downstream, _) = await RunAsync(settings, headers);

        Assert.Single(downstream);
        Assert.IsType<Http3HeadersFrame>(downstream[0]);
    }
}
