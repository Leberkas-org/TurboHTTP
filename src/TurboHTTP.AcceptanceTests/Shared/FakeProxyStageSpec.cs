using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Shared;

public sealed class FakeProxyStageSpec : EngineTestBase
{
    private static ConnectTransport MakeConnectTransport() => new(new TcpTransportOptions
    {
        Host = "target.example.com",
        Port = 443
    });

    [Fact(Timeout = 5000)]
    public async Task FakeProxy_should_respond_with_200_connection_established_when_connect_item_arrives()
    {
        var requestBytes = Encoding.Latin1.GetBytes("GET /hello HTTP/1.1\r\nHost: target.example.com\r\n\r\n");
        const string responseBody = "Hello Tunnel";
        var tunnelResponseBytes = Encoding.Latin1.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {responseBody.Length}\r\n\r\n{responseBody}");

        var fake = new FakeProxyStage((_, _) => tunnelResponseBytes);
        var flow = Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake);

        var items = new ITransportOutbound[]
        {
            MakeConnectTransport(),
            new TransportData(TransportBufferTestExtensions.FromArray(requestBytes))
        };

        var results = new List<ITransportInbound>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(items)
            .Via(flow)
            .RunWith(Sink.ForEach<ITransportInbound>(item =>
            {
                results.Add(item);
                if (results.Count == 2)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);

        var connectResponse = Assert.IsType<TransportData>(results[0]);
        var connectResponseText = Encoding.Latin1.GetString(connectResponse.Buffer.Span);
        Assert.Contains("200 Connection Established", connectResponseText);

        var tunnelResponse = Assert.IsType<TransportData>(results[1]);
        var tunnelResponseText = Encoding.Latin1.GetString(tunnelResponse.Buffer.Span);
        Assert.Contains("Hello Tunnel", tunnelResponseText);
    }

    [Fact(Timeout = 5000)]
    public async Task FakeProxy_should_expose_tunneled_request_bytes_via_channel()
    {
        var requestBytes = Encoding.Latin1.GetBytes("GET /inspect HTTP/1.1\r\nHost: target.example.com\r\n\r\n");
        var responseBytes = Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");

        var fake = new FakeProxyStage((_, _) => responseBytes);
        var flow = Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake);

        var items = new ITransportOutbound[]
        {
            MakeConnectTransport(),
            new TransportData(TransportBufferTestExtensions.FromArray(requestBytes))
        };

        var tcs = new TaskCompletionSource();

        _ = Source.From(items)
            .Via(flow)
            .RunWith(Sink.ForEach<ITransportInbound>(_ =>
            {
                if (fake.OutboundChannel.Reader.Count >= 1)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
        }

        Assert.Contains("GET /inspect HTTP/1.1", rawBuilder.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task FakeProxy_should_abort_stream_when_factory_returns_null_after_tunnel()
    {
        var firstRequest = Encoding.Latin1.GetBytes("GET /first HTTP/1.1\r\nHost: target.example.com\r\n\r\n");
        var secondRequest = Encoding.Latin1.GetBytes("GET /second HTTP/1.1\r\nHost: target.example.com\r\n\r\n");

        var fake = new FakeProxyStage((index, _) =>
        {
            if (index == 0)
            {
                return Encoding.Latin1.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");
            }

            return null;
        });

        var flow = Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake);

        var items = new ITransportOutbound[]
        {
            MakeConnectTransport(),
            new TransportData(TransportBufferTestExtensions.FromArray(firstRequest)),
            new TransportData(TransportBufferTestExtensions.FromArray(secondRequest))
        };

        var results = new List<ITransportInbound>();
        var completionTcs = new TaskCompletionSource();

        _ = Source.From(items)
            .Via(flow)
            .RunWith(Sink.ForEach<ITransportInbound>(item => results.Add(item)), Materializer)
            .ContinueWith(_ => completionTcs.TrySetResult());

        await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // ConnectItem response + first tunneled response; second request aborts the stage
        Assert.Equal(2, results.Count);

        var connectResponse = Assert.IsType<TransportData>(results[0]);
        Assert.Contains("200 Connection Established", Encoding.Latin1.GetString(connectResponse.Buffer.Span));

        var firstResponse = Assert.IsType<TransportData>(results[1]);
        Assert.Contains("ok", Encoding.Latin1.GetString(firstResponse.Buffer.Span));
    }
}

