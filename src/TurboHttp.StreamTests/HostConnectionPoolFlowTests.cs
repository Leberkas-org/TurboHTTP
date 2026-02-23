using System.Buffers;
using System.Net;
using System.Text;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;

namespace TurboHttp.StreamTests;

public sealed class FakeEngine
{
    private readonly Version _version;

    public FakeEngine(Version version)
    {
        _version = version;
    }

    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int),
        (IMemoryOwner<byte>, int), HttpResponseMessage, NotUsed> CreateFlow()
    {
        var version = _version;

        var outbound = Flow.Create<HttpRequestMessage>().Select(req =>
        {
            req.Headers.TryGetValues("x-correlation-id", out var vals);
            var correlationId = string.Join("", vals ?? []);
            var bytes = Encoding.UTF8.GetBytes(correlationId);
            IMemoryOwner<byte> owner = new SimpleMemoryOwner(bytes);
            return (owner, bytes.Length);
        });

        var inbound = Flow.Create<(IMemoryOwner<byte>, int)>().Select(tuple =>
        {
            var (owner, length) = tuple;
            var correlationId = Encoding.UTF8.GetString(owner.Memory.Span[..length]);
            owner.Dispose();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = version
            };
            response.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);
            return response;
        });

        return BidiFlow.FromFlows(outbound, inbound);
    }
}

internal static class TestFlowBuilder
{
    public static (
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> Flow,
        FakeConnectionStage Fake10,
        FakeConnectionStage Fake11,
        FakeConnectionStage Fake20
        ) Build()
    {
        var fake10 = new FakeConnectionStage();
        var fake11 = new FakeConnectionStage();
        var fake20 = new FakeConnectionStage();

        var flow = Flow.FromGraph(GraphDsl.Create(builder =>
        {
            var partition = builder.Add(new Partition<HttpRequestMessage>(3, msg => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0
            }));

            var hub = builder.Add(new Merge<HttpResponseMessage>(3));

            var http10 = builder.Add(
                new FakeEngine(HttpVersion.Version10).CreateFlow()
                    .Join(Flow.FromGraph(fake10)));

            var http11 = builder.Add(
                new FakeEngine(HttpVersion.Version11).CreateFlow()
                    .Join(Flow.FromGraph(fake11)));

            var http20 = builder.Add(
                new FakeEngine(HttpVersion.Version20).CreateFlow()
                    .Join(Flow.FromGraph(fake20)));

            builder.From(partition.Out(0)).Via(http10).To(hub.In(0));
            builder.From(partition.Out(1)).Via(http11).To(hub.In(1));
            builder.From(partition.Out(2)).Via(http20).To(hub.In(2));

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, hub.Out);
        }));

        return (flow, fake10, fake11, fake20);
    }
}

public sealed class HostConnectionPoolFlowTests : TestKit
{
    private readonly IMaterializer _materializer;

    public HostConnectionPoolFlowTests() : base(ActorSystem.Create("test"))
    {
        _materializer = Sys.Materializer();
    }

    private async Task<HttpResponseMessage> SendRequest(
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> flow,
        Version version,
        string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com")
        {
            Version = version
        };
        request.Headers.TryAddWithoutValidation("x-correlation-id", correlationId);

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                if (res.Headers.TryGetValues("x-correlation-id", out var vals) &&
                    string.Join("", vals) == correlationId)
                {
                    tcs.TrySetResult(res);
                }
            }), _materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Http10_Request_Returns_Response()
    {
        var (flow, _, _, _) = TestFlowBuilder.Build();
        var correlationId = Guid.NewGuid().ToString();

        var response = await SendRequest(flow, HttpVersion.Version10, correlationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
        Assert.True(response.Headers.TryGetValues("x-correlation-id", out var vals));
        Assert.Contains(correlationId, vals);
    }

    [Fact]
    public async Task Http11_Request_Returns_Response()
    {
        var (flow, _, _, _) = TestFlowBuilder.Build();
        var correlationId = Guid.NewGuid().ToString();

        var response = await SendRequest(flow, HttpVersion.Version11, correlationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.True(response.Headers.TryGetValues("x-correlation-id", out var vals));
        Assert.Contains(correlationId, vals);
    }

    [Fact]
    public async Task Http20_Request_Returns_Response()
    {
        var (flow, _, _, _) = TestFlowBuilder.Build();
        var correlationId = Guid.NewGuid().ToString();

        var response = await SendRequest(flow, HttpVersion.Version20, correlationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.True(response.Headers.TryGetValues("x-correlation-id", out var vals));
        Assert.Contains(correlationId, vals);
    }

    [Fact]
    public async Task Http10_Uses_Correct_FakeConnection()
    {
        var (flow, fake10, fake11, fake20) = TestFlowBuilder.Build();
        var correlationId = Guid.NewGuid().ToString();

        await SendRequest(flow, HttpVersion.Version10, correlationId);

        Assert.True(fake10.OutboundChannel.Reader.TryRead(out var chunk));
        var written = Encoding.UTF8.GetString(chunk.Item1.Memory.Span[..chunk.Item2]);
        Assert.Equal(correlationId, written);

        Assert.False(fake11.OutboundChannel.Reader.TryRead(out _));
        Assert.False(fake20.OutboundChannel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Http11_Uses_Correct_FakeConnection()
    {
        var (flow, fake10, fake11, fake20) = TestFlowBuilder.Build();
        var correlationId = Guid.NewGuid().ToString();

        await SendRequest(flow, HttpVersion.Version11, correlationId);

        Assert.True(fake11.OutboundChannel.Reader.TryRead(out var chunk));
        var written = Encoding.UTF8.GetString(chunk.Item1.Memory.Span[..chunk.Item2]);
        Assert.Equal(correlationId, written);

        Assert.False(fake10.OutboundChannel.Reader.TryRead(out _));
        Assert.False(fake20.OutboundChannel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Http20_Uses_Correct_FakeConnection()
    {
        var (flow, fake10, fake11, fake20) = TestFlowBuilder.Build();
        var correlationId = Guid.NewGuid().ToString();

        await SendRequest(flow, HttpVersion.Version20, correlationId);

        Assert.True(fake20.OutboundChannel.Reader.TryRead(out var chunk));
        var written = Encoding.UTF8.GetString(chunk.Item1.Memory.Span[..chunk.Item2]);
        Assert.Equal(correlationId, written);

        Assert.False(fake10.OutboundChannel.Reader.TryRead(out _));
        Assert.False(fake11.OutboundChannel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Mixed_Versions_All_Return_Correct_Responses()
    {
        var (flow, _, _, _) = TestFlowBuilder.Build();

        var id10 = Guid.NewGuid().ToString();
        var id11 = Guid.NewGuid().ToString();
        var id20 = Guid.NewGuid().ToString();

        var t10Task = SendRequest(flow, HttpVersion.Version10, id10);
        var t11Task = SendRequest(flow, HttpVersion.Version11, id11);
        var t20Task = SendRequest(flow, HttpVersion.Version20, id20);

        var t20 = await t20Task;
        var t11 = await t11Task;
        var t10 = await t10Task;

        Assert.Equal(HttpVersion.Version10, t10.Version);
        Assert.Equal(HttpVersion.Version11, t11.Version);
        Assert.Equal(HttpVersion.Version20, t20.Version);

        Assert.True(t10.Headers.TryGetValues("x-correlation-id", out var v10));
        Assert.Contains(id10, v10);

        Assert.True(t11.Headers.TryGetValues("x-correlation-id", out var v11));
        Assert.Contains(id11, v11);

        Assert.True(t20.Headers.TryGetValues("x-correlation-id", out var v20));
        Assert.Contains(id20, v20);
    }
}