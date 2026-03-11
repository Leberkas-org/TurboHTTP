using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http10;

/// <summary>
/// Integration tests for Http10Engine content encoding (decompression).
/// Verifies that the Http10Decoder transparently decompresses gzip/deflate responses
/// (RFC 9110 §8.4), passes through identity encoding, removes the Content-Encoding
/// header after decompression, and updates Content-Length to the decompressed size.
/// </summary>
public sealed class Http10ContentEncodingTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http10ContentEncodingTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.0 request through the Http10Engine pipeline.
    /// Each call materialises a fresh pipeline (HTTP/1.0 closes connection after response).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http10Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Generates the same deterministic payload the server produces.
    /// </summary>
    private static byte[] GenerateExpectedPayload(int kb)
    {
        var size = kb * 1024;
        var data = new byte[size];
        for (var i = 0; i < size; i++)
        {
            data[i] = (byte)('A' + (i % 26));
        }

        return data;
    }

    [Fact(DisplayName = "CE-INT-001: GET /compress/gzip/1 decompresses gzip response")]
    public async Task Gzip_Decompressed()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/gzip/1")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "CE-INT-002: GET /compress/deflate/1 decompresses deflate response")]
    public async Task Deflate_Decompressed()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/deflate/1")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "CE-INT-003: GET /compress/identity/1 passes through uncompressed")]
    public async Task Identity_Passthrough()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/identity/1")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
        // Identity responses should not have Content-Encoding header
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "CE-INT-004: Content-Encoding header removed after gzip decompression")]
    public async Task ContentEncoding_Removed_After_Decompression()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/gzip/1")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        // Http10Decoder decompresses the body and strips Content-Encoding (RFC 9110 §8.4)
        Assert.Empty(response.Content.Headers.ContentEncoding);
        // Body is already decompressed
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "CE-INT-005: Content-Length updated to decompressed size after gzip")]
    public async Task ContentLength_Updated_After_Decompression()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/gzip/1")
        {
            Version = HttpVersion.Version10
        };

        var response = await SendAsync(request);

        // Http10Decoder updates Content-Length to the decompressed body size
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024, body.Length); // 1 KB decompressed
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
    }
}
