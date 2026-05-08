using System.Net;
using TurboHTTP.IntegrationTests.Kestrel.Shared;

namespace TurboHTTP.IntegrationTests.Kestrel.Protocol;

public sealed class TransferSpec : FeatureSpecBase
{
    public TransferSpec(ServerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture) { }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Transfer_should_receive_large_body(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/large/256"), ct);

        var content = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(256 * 1024, content.Length);
    }

    [Theory(Timeout = 15000)]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.Created)]
    [InlineData(204, HttpStatusCode.NoContent)]
    [InlineData(400, HttpStatusCode.BadRequest)]
    [InlineData(404, HttpStatusCode.NotFound)]
    [InlineData(418, (HttpStatusCode)418)]
    [InlineData(500, HttpStatusCode.InternalServerError)]
    [InlineData(503, HttpStatusCode.ServiceUnavailable)]
    public async Task Transfer_should_return_correct_status_code(int code, HttpStatusCode expected)
    {
        await using var helper = CreateClient(HttpProtocol.H11);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/status/{code}"), ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Transfer_should_handle_empty_body_with_content_length_zero(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/empty-cl"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(PlaintextProtocols))]
    public async Task Transfer_should_receive_chunked_response(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/chunked/4"), ct);

        var content = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4 * 1024, content.Length);
    }

    [Theory(Timeout = 15000)]
    [MemberData(nameof(Protocols))]
    public async Task Transfer_should_echo_large_post_body(HttpProtocol protocol)
    {
        await using var helper = CreateClient(protocol);
        var ct = TestContext.Current.CancellationToken;

        var payload = new string('X', 8192);
        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/echo")
            {
                Content = new StringContent(payload)
            }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, await response.Content.ReadAsStringAsync(ct));
    }
}
