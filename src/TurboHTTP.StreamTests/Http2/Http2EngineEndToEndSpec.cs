using System.IO.Compression;
using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Streams;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.StreamTests.Http2;

/// <summary>
/// Round-trip tests for the HTTP/2 engine per RFC 9113.
/// Verifies end-to-end request encoding and response decoding through the full HTTP/2 protocol flow including HPACK.
/// </summary>
[Trait("RFC", "RFC9113")]
public sealed class Http2EngineEndToEndSpec : EngineTestBase
{
    private static Http20Engine Engine => new();

    private readonly HpackEncoder _hpack = new(useHuffman: false);

    private ReadOnlyMemory<byte> EncodeResponseHeaders(params (string Name, string Value)[] headers)
        => _hpack.Encode(headers);

    private static byte[] ServerSettings()
        => new SettingsFrame([]).Serialize();

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2Engine_should_return_200_response_when_get_request_round_trips_with_settings_and_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/hello")
        {
            Version = HttpVersion.Version20
        };

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var (response, outboundFrames) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is HeadersFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2Engine_should_emit_headers_and_data_frames_when_post_request_with_body_encoded()
    {
        const string payload = "field=value";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var (response, outboundFrames) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(outboundFrames, f => f is HeadersFrame);
        Assert.Contains(outboundFrames, f => f is DataFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Http2Engine_should_preserve_raw_gzip_body_when_content_encoding_is_gzip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data")
        {
            Version = HttpVersion.Version20
        };

        var originalBody = "Hello, compressed HTTP/2 world!"u8.ToArray();
        byte[] compressedBody;
        using (var ms = new MemoryStream())
        {
            await using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(originalBody);
            }

            compressedBody = ms.ToArray();
        }

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders(
                (":status", "200"),
                ("content-encoding", "gzip")),
            endStream: false,
            endHeaders: true).Serialize();

        var dataFrame = new DataFrame(
            streamId: 1,
            data: compressedBody,
            endStream: true).Serialize();

        // Concatenate headers + data into a single server frame buffer so that
        // the fake stage can serve them in one push (only 2 unlock events available
        // for a GET: client HEADERS + SETTINGS ACK).
        var responseFrames = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(responseFrames, 0);
        dataFrame.CopyTo(responseFrames, headersFrame.Length);

        var (response, _) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            responseFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Protocol engine must NOT decompress — raw compressed bytes preserved for feature layer
        var body = await response.Content!.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(compressedBody, body);
        Assert.Equal("gzip", response.Content!.Headers.GetValues("Content-Encoding").Single());
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.5")]
    public async Task Http2Engine_should_emit_settings_ack_when_server_settings_received()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/ack-test")
        {
            Version = HttpVersion.Version20
        };

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var (response, outboundFrames) = await SendH2EngineAsync(
            Engine.CreateFlow(),
            request,
            ServerSettings(),
            headersFrame);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ack = outboundFrames.OfType<SettingsFrame>().FirstOrDefault(f => f.IsAck);
        Assert.NotNull(ack);
        Assert.Empty(ack.Parameters);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public async Task Http2Engine_should_produce_three_responses_with_stream_ids_1_3_5_when_three_requests_sent()
    {
        const int count = 3;
        var requests = Enumerable.Range(1, count)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, "http://example.com/item")
            {
                Version = HttpVersion.Version20
            })
            .ToList();

        // Use a single encoder so the HPACK dynamic table stays in sync
        // with the shared decoder inside Http20StreamStage.
        var enc = new HpackEncoder(useHuffman: false);
        var h1 = new HeadersFrame(streamId: 1,
            headerBlock: enc.Encode([(":status", "200")]),
            endStream: true, endHeaders: true).Serialize();
        var h3 = new HeadersFrame(streamId: 3,
            headerBlock: enc.Encode([(":status", "200")]),
            endStream: true, endHeaders: true).Serialize();
        var h5 = new HeadersFrame(streamId: 5,
            headerBlock: enc.Encode([(":status", "200")]),
            endStream: true, endHeaders: true).Serialize();

        var (responses, outboundFrames) = await SendH2EngineAsyncMany(
            Engine.CreateFlow(),
            requests,
            count,
            ServerSettings(),
            h1, h3, h5);

        Assert.Equal(count, responses.Count);

        var outboundHeaders = outboundFrames.OfType<HeadersFrame>().ToList();
        Assert.Equal(count, outboundHeaders.Count);

        // Stream IDs must be 1, 3, 5 (client-side odd IDs, ascending)
        var streamIds = outboundHeaders.Select(f => f.StreamId).OrderBy(id => id).ToList();
        Assert.Equal(new[] { 1, 3, 5 }, streamIds);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.5")]
    public async Task Http2Engine_should_produce_max_concurrent_streams_signal_when_settings_max_concurrent_streams_received()
    {
        var engine = new Http20Engine();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/signal-test")
        {
            Version = HttpVersion.Version20
        };

        // Server sends SETTINGS with MAX_CONCURRENT_STREAMS = 50, then response headers
        var settingsFrame = new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 50u)]).Serialize();

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var signalTcs = new TaskCompletionSource<MaxConcurrentStreamsItem>();
        var responseTcs = new TaskCompletionSource<HttpResponseMessage>();

        var bidiFlow = engine.CreateFlow();

        var fakeStage = new H2EngineFakeConnectionStage(settingsFrame, headersFrame);

        var capturingFake = Flow.Create<IOutputItem>()
            .Select(item =>
            {
                if (item is MaxConcurrentStreamsItem mcs)
                {
                    signalTcs.TrySetResult(mcs);
                }

                return item;
            })
            .Via(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fakeStage));

        var flow = bidiFlow.Join(capturingFake);

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => responseTcs.TrySetResult(res)), Materializer);

        var response = await responseTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var signalItem = await signalTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(signalItem);
        Assert.Equal(50, signalItem.MaxStreams);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-3.4")]
    public async Task Http2Engine_should_emit_connection_preface_when_first_connect_item_arrives()
    {
        var engine = new Http20Engine();
        var bidiFlow = engine.CreateFlow();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/preface-test")
        {
            Version = HttpVersion.Version20
        };

        var headersFrame = new HeadersFrame(
            streamId: 1,
            headerBlock: EncodeResponseHeaders((":status", "200")),
            endStream: true,
            endHeaders: true).Serialize();

        var connectItem = new ConnectItem(new TcpOptions { Host = "example.com", Port = 80 })
        {
            Key = new RequestEndpoint
            {
                Scheme = "http",
                Host = "example.com",
                Port = 80,
                Version = HttpVersion.Version20
            }
        };

        var capturedByteSnapshots = new List<byte[]>();
        var fakeStage = new H2EngineFakeConnectionStage(ServerSettings(), headersFrame);
        var responseTcs = new TaskCompletionSource<HttpResponseMessage>();

        // Build a custom flow that injects ConnectItem (via MergePreferred) before engine output,
        // captures DataItem bytes, then feeds to fake TCP.
        // Preface is now emitted by Http20EncoderStage on its first pull (inlined from PrependPrefaceStage).
        var customFlow = Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(GraphDsl.Create(b =>
        {
            var merge = b.Add(new MergePreferred<IOutputItem>(1));
            var connectSrc = b.Add(Source.Single<IOutputItem>(connectItem));
            var capture = b.Add(Flow.Create<IOutputItem>()
                .Select(item =>
                {
                    if (item is NetworkBuffer d)
                    {
                        capturedByteSnapshots.Add(d.Span.ToArray());
                    }

                    return item;
                }));
            var fake = b.Add(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fakeStage));

            b.From(connectSrc.Outlet).To(merge.Preferred);
            b.From(merge.Out).To(capture.Inlet);
            b.From(capture.Outlet).To(fake.Inlet);

            return new FlowShape<IOutputItem, IInputItem>(merge.In(0), fake.Outlet);
        }));

        var flow = bidiFlow.Join(customFlow);

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => responseTcs.TrySetResult(res)), Materializer);

        var response = await responseTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify that a captured DataItem begins with the 24-byte HTTP/2 magic
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        var hasPrefaceMagic = capturedByteSnapshots.Exists(
            bytes => bytes.Length >= 24 && bytes.AsSpan(0, 24).SequenceEqual(magic));
        Assert.True(hasPrefaceMagic, "Expected outbound bytes to contain the 24-byte HTTP/2 connection preface magic");
    }
}
