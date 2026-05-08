using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Protocol;

public sealed class ConcurrencySpec : FeatureSpecBase
{
    public ConcurrencySpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Protocols))]
    public async Task Concurrency_should_succeed_with_parallel_gets(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var tasks = Enumerable.Range(0, 5).Select(_ =>
            helper.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), ct));

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Protocols))]
    public async Task Concurrency_should_succeed_with_sequential_burst(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 10; i++)
        {
            var response = await helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/hello"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Protocols))]
    public async Task Concurrency_should_handle_mixed_methods(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var getTasks = Enumerable.Range(0, 3).Select(_ =>
            helper.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), ct));
        var postTasks = Enumerable.Range(0, 3).Select(_ =>
            helper.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/echo")
            {
                Content = new StringContent("test")
            }, ct));

        var responses = await Task.WhenAll(getTasks.Concat(postTasks));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
