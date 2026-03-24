using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H2;

[Collection("H2")]
public sealed class ErrorHandlingIntegrationTests
{
    private readonly KestrelH2Fixture _fixture;

    public ErrorHandlingIntegrationTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(_fixture.Port, new Version(2, 0));
    }

    [Fact(DisplayName = "Error-H2-001: RST_STREAM abort raises exception")]
    public async Task RstStream_Abort_Raises_Exception()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h2/abort");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            var response = await helper.Client.SendAsync(request, cts.Token);
            await response.Content.ReadAsStringAsync(cts.Token);
        });
    }

    [Fact(DisplayName = "Error-H2-002: Delay route completes after server-side wait")]
    public async Task Delay_Route_Completes_After_Wait()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h2/delay/500");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("delayed", body);
    }

    [Fact(DisplayName = "Error-H2-003: Timeout cancellation aborts in-flight HTTP/2 request")]
    public async Task Timeout_Cancellation_Aborts_InFlight_Request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h2/delay/10000");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await helper.Client.SendAsync(request, cts.Token));
    }

    [Theory(DisplayName = "Error-H2-004: 4xx status codes returned as response over HTTP/2")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task Status_4xx_Returned_As_Response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Theory(DisplayName = "Error-H2-005: 5xx status codes returned as response over HTTP/2")]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Status_5xx_Returned_As_Response(int statusCode)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/status/{statusCode}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
    }

    [Fact(DisplayName = "Error-H2-006: 20 custom response headers decoded correctly")]
    public async Task Many_Headers_Decoded_Correctly()
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
            var headerName = $"X-Custom-{i:D3}";
            Assert.True(response.Headers.TryGetValues(headerName, out var values),
                $"Missing header {headerName}");
            Assert.Equal($"value-{i:D3}", string.Join("", values));
        }
    }

    [Fact(DisplayName = "Error-H2-007: Binary body roundtrip over HTTP/2")]
    public async Task Binary_Body_Roundtrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var payload = new byte[256];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)i;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/h2/echo-binary")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(payload, body);
    }

    [Theory(DisplayName = "Error-H2-008: Large HPACK-compressed headers received correctly")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Large_Headers_Received_Correctly(int kb)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/h2/large-headers/{kb}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify body size
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(kb * 1024, body.Length);

        // Verify all 10 custom headers are present with 90-char values
        for (var i = 0; i < 10; i++)
        {
            var headerName = $"X-Large-{i:D2}";
            Assert.True(response.Headers.TryGetValues(headerName, out var values),
                $"Missing header {headerName}");
            Assert.Equal(90, string.Join("", values).Length);
        }
    }
}
