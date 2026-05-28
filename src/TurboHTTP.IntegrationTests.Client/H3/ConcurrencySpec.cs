using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H3;

[Collection("H3")]
public sealed class ConcurrencySpec : IntegrationSpecBase
{
    public ConcurrencySpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H3, tls: true);

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_multiplex_parallel_gets()
    {
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_mixed_methods_multiplexed()
    {
        var getTasks = Enumerable.Range(0, 5).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken));

        var postTasks = Enumerable.Range(0, 5).Select(i =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/post")
                {
                    Content = new StringContent($"body-{i}", Encoding.UTF8)
                }, CancellationToken));

        var responses = await Task.WhenAll(getTasks.Concat(postTasks));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_parallel_large_bodies()
    {
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/bytes/16384"), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
            Assert.Equal(16384, content.Length);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_succeed_with_sequential_burst()
    {
        for (var i = 0; i < 20; i++)
        {
            var response = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}