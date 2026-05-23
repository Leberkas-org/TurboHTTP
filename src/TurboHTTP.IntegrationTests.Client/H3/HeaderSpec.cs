using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H3;

[Collection("H3")]
public sealed class HeaderSpec : IntegrationSpecBase
{
    public HeaderSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H3, Tls: true);

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_custom_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h3");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("turbohttp-h3", headers.GetHeaderValue("X-Custom-Test"));
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