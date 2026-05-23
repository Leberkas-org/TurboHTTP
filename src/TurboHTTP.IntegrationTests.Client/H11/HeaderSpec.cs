using System.Net;
using System.Text.Json;
using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
public sealed class HeaderSpec : IntegrationSpecBase
{
    public HeaderSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    protected override ProtocolVariant Variant => new(TestHttpVersion.H11, Tls: false);

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_custom_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Custom-Test", "turbohttp-h11");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("turbohttp-h11", headers.GetHeaderValue("X-Custom-Test"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_multiple_custom_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-First", "one");
        request.Headers.Add("X-Second", "two");
        request.Headers.Add("X-Third", "three");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("one", headers.GetHeaderValue("X-First"));
        Assert.Equal("two", headers.GetHeaderValue("X-Second"));
        Assert.Equal("three", headers.GetHeaderValue("X-Third"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_forward_user_agent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.UserAgent.ParseAdd("TurboHTTP/1.1 IntegrationTest");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Contains("TurboHTTP/1.1", headers.GetHeaderValue("User-Agent"));
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_receive_response_headers()
    {
        var response = await Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/response-headers?X-Server-Custom=test-value"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Server-Custom", out var values));
        Assert.Contains("test-value", values);
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_preserve_headers_across_keep_alive()
    {
        for (var i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
            request.Headers.Add("X-Iteration", i.ToString());

            var response = await Client.SendAsync(request, CancellationToken);
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            var json = JsonDocument.Parse(body);

            var headers = json.RootElement.GetProperty("headers");
            Assert.Equal(i.ToString(), headers.GetHeaderValue("X-Iteration"));
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Header_should_preserve_header_with_special_characters()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/headers");
        request.Headers.Add("X-Special", "value with spaces and (parens)");

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        var headers = json.RootElement.GetProperty("headers");
        Assert.Equal("value with spaces and (parens)", headers.GetHeaderValue("X-Special"));
    }
}