using System.Net;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
public sealed class ConcurrencySpec : IntegrationSpecBase
{
    public ConcurrencySpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H11, Tls: false);

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_succeed_with_parallel_gets()
    {
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_succeed_with_sequential_burst()
    {
        for (var i = 0; i < 10; i++)
        {
            var response = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_mixed_methods()
    {
        var getTasks = Enumerable.Range(0, 3).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), CancellationToken));

        var postTasks = Enumerable.Range(0, 3).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/post")
                {
                    Content = new StringContent("test")
                }, CancellationToken));

        var responses = await Task.WhenAll(getTasks.Concat(postTasks));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_parallel_different_endpoints()
    {
        var endpoints = new[] { "/get", "/headers", "/bytes/64", "/status/200", "/gzip" };

        var tasks = endpoints.Select(e =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, e), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_parallel_large_bodies()
    {
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/bytes/8192"), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);
            Assert.Equal(8192, content.Length);
        }
    }
}