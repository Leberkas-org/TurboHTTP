using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H11;

[Collection("H11")]
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
        return ClientHelper.CreateClient(_server.HttpPort, new Version(1, 1), system: _systemFixture.System);
    }

    [Fact(DisplayName = "Edge-001: Chunked response with trailers delivers complete body")]
    public async Task Chunked_Response_With_Trailers_Delivers_Body()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/chunked/trailer");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("chunked-with-trailer", body);
    }

    [Fact(DisplayName = "Edge-002: Chunked response with exact boundaries delivers all bytes")]
    public async Task Chunked_Response_Exact_Boundaries_Delivers_All_Bytes()
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

    [Fact(DisplayName = "Edge-003: Chunked response with Content-MD5 trailer received without error")]
    public async Task Chunked_Response_With_Md5_Trailer_Received()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/chunked/md5");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("checksum-body", body);
    }

    [Fact(DisplayName = "Edge-004: POST with chunked request body echoed correctly")]
    public async Task Post_Chunked_Request_Body_Echoed()
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

    [Fact(DisplayName = "Edge-005: Large body 256KB received intact")]
    public async Task Large_Body_256Kb_Received_Intact()
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

    [Fact(DisplayName = "Edge-006: Multiple response headers with same name all accessible")]
    public async Task Multiple_Headers_With_Same_Name_All_Accessible()
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

    [Fact(DisplayName = "Edge-007: Form URL-encoded POST returns correct received length")]
    public async Task Form_Urlencoded_Post_Returns_Received_Length()
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

    [Fact(DisplayName = "Edge-008: Range request returns 206 Partial Content with correct bytes")]
    public async Task Range_Request_Returns_206_Partial_Content()
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
