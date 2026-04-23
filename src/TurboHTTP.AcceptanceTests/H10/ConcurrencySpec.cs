using System.Net;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.IO;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class ConcurrencySpec : AcceptanceTestBase
{
    private static byte[] BuildResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {(int)status} {status}\r\n");
        sb.Append($"Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\n");
        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private async Task<HttpResponseMessage> SendScriptedAsync(HttpRequestMessage request,
        Func<int, byte[], byte[]?> factory)
    {
        var fake = new ScriptedFakeConnectionStage(factory);
        var flow = CreateHttp10Engine().CreateFlow().Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Concurrency_should_succeed_with_three_parallel_gets()
    {
        var tasks = Enumerable.Range(0, 3).Select(async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping")
            {
                Version = HttpVersion.Version10
            };
            return await SendScriptedAsync(request, (_, _) => BuildResponse("pong"));
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(3, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Concurrency_should_succeed_with_sequential_burst_of_10_requests()
    {
        for (var i = 0; i < 10; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
            {
                Version = HttpVersion.Version10
            };

            var response = await SendScriptedAsync(request, (_, _) => BuildResponse("Hello World"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task Concurrency_should_succeed_with_mixed_get_and_post_concurrent_requests()
    {
        var getTask1 = SendScriptedAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello") { Version = HttpVersion.Version10 },
            (_, _) => BuildResponse("Hello World"));

        var getTask2 = SendScriptedAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/ping") { Version = HttpVersion.Version10 },
            (_, _) => BuildResponse("pong"));

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/echo")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent("h10-post", Encoding.UTF8, "text/plain")
        };
        var postTask = SendScriptedAsync(postRequest, (_, _) => BuildResponse("h10-post"));

        var responses = await Task.WhenAll(getTask1, getTask2, postTask);

        Assert.Equal(3, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}