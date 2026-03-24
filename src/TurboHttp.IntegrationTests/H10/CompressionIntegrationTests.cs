using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H10;

[Collection("H10")]
public sealed class CompressionIntegrationTests
{
    private readonly KestrelFixture _fixture;

    public CompressionIntegrationTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    private ClientHelper CreateClient()
    {
        return ClientHelper.CreateClient(
            _fixture.Port,
            new Version(1, 0));
    }

    [Fact(DisplayName = "Compression-H10-001: gzip response transparently decompressed to original size")]
    public async Task Gzip_Response_Transparently_Decompressed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/gzip/4");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(4 * 1024, body.Length);

        // Verify content matches the expected repeating ASCII pattern
        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(DisplayName = "Compression-H10-002: deflate response transparently decompressed")]
    public async Task Deflate_Response_Transparently_Decompressed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/deflate/2");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(2 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(DisplayName = "Compression-H10-003: brotli response transparently decompressed")]
    public async Task Brotli_Response_Transparently_Decompressed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/br/3");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(3 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(DisplayName = "Compression-H10-004: identity encoding passes through unchanged")]
    public async Task Identity_Encoding_Passes_Through_Unchanged()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/identity/1");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
        Assert.Equal(1 * 1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(DisplayName = "Compression-H10-005: content negotiation with Accept-Encoding gzip returns gzip response")]
    public async Task Negotiate_AcceptEncoding_Gzip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);

        // Negotiate uses 1 KB payload — decompressed body should be 1024 bytes
        Assert.Equal(1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(DisplayName = "Compression-H10-006: content negotiation with Accept-Encoding br returns brotli response")]
    public async Task Negotiate_AcceptEncoding_Brotli()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);

        Assert.Equal(1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }

    [Fact(DisplayName = "Compression-H10-007: content negotiation with no Accept-Encoding returns identity")]
    public async Task Negotiate_NoAcceptEncoding_Returns_Identity()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var helper = CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/compress/negotiate");
        // Explicitly clear Accept-Encoding to ensure no encoding is requested
        request.Headers.Remove("Accept-Encoding");
        var response = await helper.Client.SendAsync(request, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(cts.Token);

        // Identity (no compression) — body should be raw 1024 bytes
        Assert.Equal(1024, body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            Assert.Equal((byte)('A' + i % 26), body[i]);
        }
    }
}
