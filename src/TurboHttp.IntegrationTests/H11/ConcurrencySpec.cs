using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H11;

[Collection("H11")]
public sealed class ConcurrencySpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ConcurrencySpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.HttpPort, new Version(1, 1), system: _systemFixture.System);
    }

    [Fact(Timeout = 60000)]
    public async Task Concurrency_should_succeed_with_5_parallel_gets()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var helpers = Enumerable.Range(0, 5).Select(_ => CreateClient()).ToArray();
        try
        {
            var tasks = helpers
                .Select(h => h.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"), cts.Token))
                .ToArray();

            var responses = await Task.WhenAll(tasks);

            Assert.Equal(5, responses.Length);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
        finally
        {
            foreach (var h in helpers)
            {
                await h.DisposeAsync();
            }
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Concurrency_should_succeed_with_3_parallel_posts()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var payloads = Enumerable.Range(0, 3).Select(i => $"body-{i}").ToArray();
        var helpers = payloads.Select(_ => CreateClient()).ToArray();
        try
        {
            var tasks = helpers
                .Zip(payloads, (h, payload) =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "text/plain")
                    };
                    return h.Client.SendAsync(request, cts.Token);
                })
                .ToArray();

            var responses = await Task.WhenAll(tasks);

            Assert.Equal(3, responses.Length);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
        finally
        {
            foreach (var h in helpers)
            {
                await h.DisposeAsync();
            }
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Concurrency_should_succeed_with_sequential_burst_of_20_requests()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var helper = CreateClient();

        for (var i = 0; i < 20; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
            var response = await helper.Client.SendAsync(request, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Concurrency_should_succeed_with_mixed_methods_concurrent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var helpers = Enumerable.Range(0, 5).Select(_ => CreateClient()).ToArray();
        try
        {
            var tasks = new[]
            {
                helpers[0].Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token),
                helpers[1].Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"), cts.Token),
                helpers[2].Client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, "/echo")
                    {
                        Content = new StringContent("concurrent-post", Encoding.UTF8, "text/plain")
                    }, cts.Token),
                helpers[3].Client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Put, "/echo")
                    {
                        Content = new StringContent("concurrent-put", Encoding.UTF8, "text/plain")
                    }, cts.Token),
                helpers[4].Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token)
            };

            var responses = await Task.WhenAll(tasks);

            Assert.Equal(5, responses.Length);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
        finally
        {
            foreach (var h in helpers)
            {
                await h.DisposeAsync();
            }
        }
    }
}
