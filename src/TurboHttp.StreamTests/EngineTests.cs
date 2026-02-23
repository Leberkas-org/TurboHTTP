using System.Buffers;
using System.Net;
using System.Text;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit.Xunit2;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests;

internal sealed class SimpleMemoryOwner : IMemoryOwner<byte>
{
    public Memory<byte> Memory { get; }
    public SimpleMemoryOwner(byte[] data) => Memory = data;

    public void Dispose()
    {
    }
}

public sealed class
    EngineFakeConnectionStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
{
    private readonly Func<byte[]> _responseFactory;

    public Channel<(IMemoryOwner<byte>, int)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    public Inlet<(IMemoryOwner<byte>, int)> In { get; } = new("fake-tcp.in");
    public Outlet<(IMemoryOwner<byte>, int)> Out { get; } = new("fake-tcp.out");

    public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape { get; }

    public EngineFakeConnectionStage(Func<byte[]> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly EngineFakeConnectionStage _stage;
        private readonly Queue<(IMemoryOwner<byte>, int)> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var (owner, length) = Grab(stage.In);

                    var copy = new byte[length];
                    owner.Memory.Span[..length].CopyTo(copy);
                    stage.OutboundChannel.Writer.TryWrite((new SimpleMemoryOwner(copy), length));
                    owner.Dispose();

                    var responseBytes = _stage._responseFactory();
                    IMemoryOwner<byte> responseOwner = new SimpleMemoryOwner(responseBytes);

                    if (_downstreamWaiting)
                    {
                        _downstreamWaiting = false;
                        Push(stage.Out, (responseOwner, responseBytes.Length));
                    }
                    else
                    {
                        _buffer.Enqueue((responseOwner, responseBytes.Length));
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var chunk))
                        Push(stage.Out, chunk);
                    else
                        _downstreamWaiting = true;
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}

public abstract class EngineTestBase : TestKit
{
    protected readonly IMaterializer Materializer;

    protected EngineTestBase() : base(ActorSystem.Create("engine-test-" + Guid.NewGuid()))
    {
        Materializer = Sys.Materializer();
    }

    protected async Task<(HttpResponseMessage Response, string RawRequest)> SendAsync(
        BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int),
            (IMemoryOwner<byte>, int), HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        Func<byte[]> responseFactory)
    {
        var fake = new EngineFakeConnectionStage(responseFactory);
        var flow = engine.Join(Flow.FromGraph<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Item1.Memory.Span[..chunk.Item2]));
        }

        return (response, rawBuilder.ToString());
    }

    protected async Task<(List<HttpResponseMessage> Responses, string RawRequests)> SendManyAsync(
        BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int),
            (IMemoryOwner<byte>, int), HttpResponseMessage, NotUsed> engine,
        IEnumerable<HttpRequestMessage> requests,
        Func<byte[]> responseFactory,
        int expectedCount)
    {
        var fake = new EngineFakeConnectionStage(responseFactory);
        var flow = engine.Join(Flow.FromGraph<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>(fake));

        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == expectedCount) tcs.TrySetResult();
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Item1.Memory.Span[..chunk.Item2]));
        }

        return (results, rawBuilder.ToString());
    }
}

public sealed class Http10EngineTests : EngineTestBase
{
    private static readonly Func<byte[]> Http10Response =
        () => "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static readonly Func<byte[]> Http10ResponseWithBody =
        () => "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

    private static Http10Engine Engine => new();

    [Fact]
    public async Task Simple_GET_Returns_200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact]
    public async Task Simple_GET_Encodes_Request_Line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo?bar=1")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.StartsWith("GET /foo?bar=1 HTTP/1.0\r\n", raw);
    }

    [Fact]
    public async Task POST_With_Body_Encodes_Content_Length()
    {
        const string body = "hello=world";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var (response, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(body)}", raw);
    }

    [Fact]
    public async Task POST_Without_Body_Encodes_Content_Length_Zero()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/empty")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Contains("Content-Length: 0", raw);
    }

    [Fact]
    public async Task Request_Does_Not_Contain_Connection_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.DoesNotContain("Connection:", raw);
    }

    [Fact]
    public async Task Request_Does_Not_Contain_Host_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.DoesNotContain("Host:", raw);
    }

    [Fact]
    public async Task Custom_Header_Is_Forwarded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "test-value");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Contains("X-Custom: test-value", raw);
    }

    [Fact]
    public async Task Response_With_Body_Is_Decoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http10ResponseWithBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact]
    public async Task Response_404_Is_Decoded_Correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => "HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Multiple_Requests_All_Return_200()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/3") { Version = HttpVersion.Version10 },
        };

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Http10Response, 3);

        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(HttpVersion.Version10, r.Version));
    }
}

public sealed class Http11EngineTests : EngineTestBase
{
    private static readonly Func<byte[]> Http11Response =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static readonly Func<byte[]> Http11ResponseWithBody =
        () => "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

    private Http11Engine Engine => new();

    [Fact]
    public async Task Simple_GET_Returns_200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact]
    public async Task Simple_GET_Encodes_Request_Line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo?bar=1")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.StartsWith("GET /foo?bar=1 HTTP/1.1\r\n", raw);
    }

    [Fact]
    public async Task GET_Contains_Host_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Contains("Host: example.com", raw);
    }

    [Fact]
    public async Task POST_With_Body_Uses_Chunked_Or_Content_Length()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("hello=world", Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var (response, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(raw.Contains("Content-Length:") || raw.Contains("Transfer-Encoding: chunked"));
    }

    [Fact]
    public async Task Response_With_Body_Is_Decoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http11ResponseWithBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact]
    public async Task Custom_Header_Is_Forwarded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "test-value");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http11Response);

        Assert.Contains("X-Custom: test-value", raw);
    }

    [Fact]
    public async Task Multiple_Pipelined_Requests_All_Return_200()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1") { Version = HttpVersion.Version11 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2") { Version = HttpVersion.Version11 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/3") { Version = HttpVersion.Version11 },
        };

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Http11Response, 3);

        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(HttpVersion.Version11, r.Version));
    }
}