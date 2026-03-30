using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
public sealed class EdgeCaseIntegrationTests
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public EdgeCaseIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_server.HttpPort, new Version(1, 0), system: _systemFixture.System);
    }

    [Fact(Timeout = 30000, DisplayName = "Edge-H10-001: Large body 256KB received intact via connection-close")]
    public async Task Large_Body_256Kb_Received_Via_Connection_Close()
    {
        // HTTP/1.0 uses connection-close to delimit body end (no chunked encoding)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/large/256");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(256 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'A', b));
    }

    [Fact(Timeout = 30000, DisplayName = "Edge-H10-002: POST body echoed back correctly")]
    public async Task Post_Body_Echoed_Correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

    [Theory(Timeout = 30000, DisplayName = "Edge-H10-003: Various status codes returned correctly")]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Status_Codes_Returned_Correctly(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Fact(Timeout = 30000, DisplayName = "Edge-H10-004: Custom X-headers echoed in response")]
    public async Task Custom_Headers_Echoed_In_Response()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

    [Fact(Timeout = 30000, DisplayName = "Edge-H10-005: Empty body response completes without hanging")]
    public async Task Empty_Body_Response_Completes()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/empty-body");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }
}
