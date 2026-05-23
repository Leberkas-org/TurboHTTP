using System.Net;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H2;

[Collection("H2")]
public sealed class HeaderSpec : IntegrationSpecBase
{
    public HeaderSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H2, Tls: true);

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_custom_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h2");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("turbohttp-h2", headers.GetHeaderValue("X-Custom-Test"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_many_custom_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");

        for (var i = 0; i < 20; i++)
        {
            request.Headers.Add($"X-Header-{i}", $"value-{i}");
        }

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal($"value-{i}", headers.GetHeaderValue($"X-Header-{i}"));
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_user_agent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.UserAgent.ParseAdd("TurboHTTP/2.0 IntegrationTest");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Contains("TurboHTTP/2.0", headers.GetHeaderValue("User-Agent"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_receive_response_headers()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/response-headers?X-Server-Custom=h2-value"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Server-Custom", out var values));
        Assert.Contains("h2-value", values);
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_handle_large_header_value()
    {
        var largeValue = new string('A', 1024);
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Large-Header", largeValue);

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal(largeValue, headers.GetHeaderValue("X-Large-Header"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_preserve_headers_across_multiplexed_requests()
    {
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
            request.Headers.Add("X-Request-Id", $"req-{i}");

            var response = await Client.SendAsync(request, CancellationToken);
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            var json = JsonDocument.Parse(body);

            var headers = json.RootElement.GetProperty("headers");
            return headers.GetHeaderValue("X-Request-Id");
        });

        var results = await Task.WhenAll(tasks);
        var sorted = results.Order().ToArray();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal($"req-{i}", sorted[i]);
        }
    }
}