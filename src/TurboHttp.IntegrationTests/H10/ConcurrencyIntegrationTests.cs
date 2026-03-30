using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
public sealed class ConcurrencyIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ConcurrencyIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.HttpPort, new Version(1, 0), system: _systemFixture.System);
    }

    [Fact(Timeout = 60000, DisplayName = "Concurrency-H10-001: 3 parallel GET requests all succeed")]
    public async Task Three_Parallel_Gets_All_Succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var helpers = Enumerable.Range(0, 3).Select(_ => CreateClient()).ToArray();
        try
        {
            var tasks = helpers
                .Select(h => h.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"), cts.Token))
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

    [Fact(Timeout = 60000, DisplayName = "Concurrency-H10-002: Sequential burst of 10 requests all succeed")]
    public async Task Sequential_Burst_10_Requests_All_Succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var helper = CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
            var response = await helper.Client.SendAsync(request, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 60000, DisplayName = "Concurrency-H10-003: Mixed GET and POST concurrent requests all succeed")]
    public async Task Mixed_Get_And_Post_Concurrent_All_Succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var helpers = Enumerable.Range(0, 3).Select(_ => CreateClient()).ToArray();
        try
        {
            var tasks = new[]
            {
                helpers[0].Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token),
                helpers[1].Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"), cts.Token),
                helpers[2].Client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, "/echo")
                    {
                        Content = new StringContent("h10-post", Encoding.UTF8, "text/plain")
                    }, cts.Token)
            };

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
}
