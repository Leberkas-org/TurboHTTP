using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H11;

[Collection("H11")]
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
        return ClientHelper.CreateClient(_server.HttpPort, new Version(1, 1), system: _systemFixture.System);
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_deliver_chunked_response_with_trailers()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/chunked/trailer");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("chunked-with-trailer", body);
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_deliver_all_bytes_with_chunked_exact_boundaries()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        // 5 chunks of 1024 bytes each = 5120 bytes total
        var request = new HttpRequestMessage(HttpMethod.Get, "/chunked/exact/5/1024");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(5 * 1024, bytes.Length);
        Assert.All(bytes, b => Assert.Equal((byte)'B', b));
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_receive_chunked_response_with_md5_trailer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/chunked/md5");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("checksum-body", body);
    }

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_echo_post_chunked_request_body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var payload = new string('Z', 4096);
        var request = new HttpRequestMessage(HttpMethod.Post, "/echo/chunked")
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain")
        };

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(payload, body);
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

    [Fact(Timeout = 20000)]
    public async Task EdgeCase_should_return_206_partial_content_for_range_request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/range/64");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);

        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(100, bytes.Length);
        // Verify byte values: body[i] = (byte)(i % 256)
        for (var i = 0; i < bytes.Length; i++)
        {
            Assert.Equal((byte)(i % 256), bytes[i]);
        }
    }
}
