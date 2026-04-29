using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class EdgeCaseSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;

    public EdgeCaseSpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_receive_many_custom_response_headers()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/h3/many-headers");
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

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_return_empty_body_for_content_length_zero()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/empty-cl");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_return_empty_for_empty_body_with_no_content()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/edge/empty-body");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("", body);
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_receive_large_body_256kb_intact()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/large/256");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(256 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'A', b));
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_echo_large_binary_post_correctly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var payload = new byte[60 * 1024];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 256);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/h3/echo-binary")
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

    [Theory(Timeout = 20000)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task EdgeCase_should_receive_large_qpack_compressed_headers(int kb)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/h3/large-headers/{kb}");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(kb * 1024, body.Length);

        for (var i = 0; i < 10; i++)
        {
            var headerName = $"X-Large-{i:D2}";
            Assert.True(response.Headers.TryGetValues(headerName, out var values),
                $"Missing header {headerName}");
            Assert.Equal(90, string.Join("", values).Length);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_access_multiple_response_headers_with_same_name()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/multiheader");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Value", out var values));
        var valueList = values.ToList();
        Assert.Contains("alpha", valueList);
        Assert.Contains("beta", valueList);
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_return_received_length_for_form_urlencoded_post()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var formData = "field1=value1&field2=value2&field3=hello+world";
        var request = new HttpRequestMessage(HttpMethod.Post, "/form/urlencoded")
        {
            Content = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.StartsWith("received:", body);
        Assert.True(int.Parse(body["received:".Length..]) > 0);
    }
}
