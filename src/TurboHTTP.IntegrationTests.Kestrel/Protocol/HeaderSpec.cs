using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Protocol;

public sealed class HeaderSpec : FeatureSpecBase
{
    public HeaderSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Header_should_echo_custom_headers(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/echo");
        request.Headers.Add("X-Custom-One", "value-one");
        request.Headers.Add("X-Custom-Two", "value-two");

        var response = await helper.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Custom-One", out var v1));
        Assert.Contains("value-one", v1);
        Assert.True(response.Headers.TryGetValues("X-Custom-Two", out var v2));
        Assert.Contains("value-two", v2);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Header_should_return_user_agent(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/user-agent");
        request.Headers.UserAgent.ParseAdd("TurboHTTP/Test");

        var response = await helper.Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.Contains("TurboHTTP/Test", body);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Header_should_return_header_count(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/count");
        request.Headers.Add("X-A", "1");
        request.Headers.Add("X-B", "2");

        var response = await helper.Client.SendAsync(request, ct);

        Assert.True(response.Headers.TryGetValues("X-Header-Count", out var values));
        var count = int.Parse(values.First());
        Assert.True(count >= 2);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Header_should_set_response_headers_from_query(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/headers/set?X-Custom=test-value"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Custom", out var values));
        Assert.Contains("test-value", values);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Header_should_receive_large_header_value(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var request = new HttpRequestMessage(HttpMethod.Get, "/headers/echo");
        request.Headers.Add("X-Large", new string('A', 1024));

        var response = await helper.Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Large", out var values));
        Assert.True(values.First().Length >= 1024);
    }
}
