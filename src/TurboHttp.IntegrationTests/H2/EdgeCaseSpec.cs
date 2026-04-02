using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H2;

[Collection("H2")]
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
        return ClientHelper.CreateClient(_server.H2Port, new Version(2, 0), system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task Large_binary_post_should_be_echoed_correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var payload = new byte[60 * 1024];
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
    public async Task Many_custom_response_headers_should_be_all_accessible()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h2/many-headers");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("many-headers", body);

        for (var i = 0; i < 20; i++)
        {
            Assert.True(
                response.Headers.TryGetValues($"X-Custom-{i:D3}", out var values),
                $"Header X-Custom-{i:D3} missing");
            Assert.Equal($"value-{i:D3}", string.Join("", values!));
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Large_hpack_headers_with_body_should_be_received_correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h2/large-headers/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(4 * 1024, bytes.Length);

        // Verify at least one of the large headers arrived
        Assert.True(response.Headers.TryGetValues("X-Large-00", out var headerValues));
        Assert.Equal(90, string.Join("", headerValues!).Length);
    }

    [Fact(Timeout = 20000)]
    public async Task Stream_priority_route_should_return_expected_payload_bytes()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h2/priority/16");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(16 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'P', b));
    }

    [Fact(Timeout = 20000)]
    public async Task Echo_path_should_return_path_and_query_string()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h2/echo-path?foo=bar&baz=qux");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("/h2/echo-path?foo=bar&baz=qux", body);
    }
}
