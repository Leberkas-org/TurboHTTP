using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H2;

[Collection("H2")]
public sealed class ConnectionSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public ConnectionSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.H2Port, new Version(2, 0), system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task Sequential_requests_should_reuse_same_connection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var response1 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body1);

        var response2 = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body2);
    }

    [Fact(Timeout = 20000)]
    public async Task Concurrent_requests_should_be_multiplexed_over_single_connection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var tasks = Enumerable.Range(0, 5).Select(_ =>
            helper.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/ping"), cts.Token));

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            Assert.Equal("pong", body);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Binary_body_post_should_be_echoed_correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var payload = new byte[4 * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/h2/echo-binary")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Fact(Timeout = 20000)]
    public async Task Multiple_endpoints_should_be_served_on_same_connection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var helloResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, helloResponse.StatusCode);
        Assert.Equal("Hello World", await helloResponse.Content.ReadAsStringAsync(cts.Token));

        var headersResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/h2/many-headers"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, headersResponse.StatusCode);
        Assert.True(headersResponse.Headers.TryGetValues("X-Custom-000", out _));

        var largeResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/large/8"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, largeResponse.StatusCode);
        var largeBody = await largeResponse.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(8 * 1024, largeBody.Length);
    }

    [Fact(Timeout = 20000)]
    public async Task Post_with_body_followed_by_get_should_work_on_same_connection()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var postPayload = "hello from http2"u8.ToArray();
        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/h2/echo-binary")
        {
            Content = new ByteArrayContent(postPayload)
        };
        postRequest.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var postResponse = await helper.Client.SendAsync(postRequest, cts.Token);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var postBody = await postResponse.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(postPayload, postBody);

        var getResponse = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/h2/echo-path?q=1"), cts.Token);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("/h2/echo-path?q=1", getBody);
    }
}
