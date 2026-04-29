using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Streams;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class ConcurrencySpec : AcceptanceTestBase
{
    private static Http11Engine Engine =>
        new(new TurboClientOptions());

    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\n");
        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private async Task<HttpResponseMessage> SendScriptedAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = Engine.CreateFlow().Join(Flow.FromGraph<ITransportOutbound, ITransportInbound, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_5_parallel_gets()
    {
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version11
            };
            return await SendScriptedAsync(request, (_, _) => BuildResponse("pong"));
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_3_parallel_posts()
    {
        var tasks = Enumerable.Range(0, 3).Select(async i =>
        {
            var payload = $"body-{i}";
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo")
            {
                Version = HttpVersion.Version11,
                Content = new StringContent(payload, Encoding.UTF8, "text/plain")
            };
            return await SendScriptedAsync(request, (_, _) => BuildResponse(payload));
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(3, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_sequential_burst_of_20_requests()
    {
        for (var i = 0; i < 20; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
            {
                Version = HttpVersion.Version11
            };

            var response = await SendScriptedAsync(request, (_, _) => BuildResponse("Hello World"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Concurrency_should_succeed_with_mixed_methods_concurrent()
    {
        var getTask1 = SendScriptedAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello") { Version = HttpVersion.Version11 },
            (_, _) => BuildResponse("Hello World"));

        var getTask2 = SendScriptedAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping") { Version = HttpVersion.Version11 },
            (_, _) => BuildResponse("pong"));

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("concurrent-post", Encoding.UTF8, "text/plain")
        };
        var postTask = SendScriptedAsync(postRequest, (_, _) => BuildResponse("concurrent-post"));

        var putRequest = new HttpRequestMessage(HttpMethod.Put, "http://localhost/echo")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("concurrent-put", Encoding.UTF8, "text/plain")
        };
        var putTask = SendScriptedAsync(putRequest, (_, _) => BuildResponse("concurrent-put"));

        var getTask3 = SendScriptedAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello") { Version = HttpVersion.Version11 },
            (_, _) => BuildResponse("Hello World"));

        var responses = await Task.WhenAll(getTask1, getTask2, postTask, putTask, getTask3);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
