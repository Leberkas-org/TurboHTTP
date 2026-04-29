using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H10;

[Collection("H10")]
public sealed class EdgeCaseSpec
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public EdgeCaseSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.H1Port, new Version(1, 0), system: _systemFixture.System);
    }

    [Fact(Timeout = 60000)]
    public async Task EdgeCase_should_receive_large_256kb_body_via_connection_close()
    {
        // HTTP/1.0 uses connection-close to delimit body end (no chunked encoding)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/large/256");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(256 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'A', b));
    }

    [Fact(Timeout = 60000)]
    public async Task EdgeCase_should_echo_post_body_correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var payload = "hello from http10";
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Theory(Timeout = 30000)]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task EdgeCase_should_return_status_codes_correctly(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Fact(Timeout = 60000)]
    public async Task EdgeCase_should_echo_custom_headers_in_response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/echo");
        request.Headers.Add("X-Custom-Test", "h10-value");
        request.Headers.Add("X-Another", "second");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Custom-Test", out var customValues));
        Assert.Equal("h10-value", string.Join("", customValues));
        Assert.True(response.Headers.TryGetValues("X-Another", out var anotherValues));
        Assert.Equal("second", string.Join("", anotherValues));
    }

    [Fact(Timeout = 60000)]
    public async Task EdgeCase_should_complete_empty_body_response_without_hanging()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/empty-body");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }
}
