using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests;

[Collection("Http1Integration")]
public sealed class Http10SmokeTests : IAsyncLifetime
{
    private readonly KestrelFixture _fixture;
    private ClientHelper? _helper;

    public Http10SmokeTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_fixture.Port, new Version(1, 0));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_Hello_Returns_200_HelloWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}

[Collection("Http1Integration")]
public sealed class Http11SmokeTests : IAsyncLifetime
{
    private readonly KestrelFixture _fixture;
    private ClientHelper? _helper;

    public Http11SmokeTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_fixture.Port, new Version(1, 1));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_Hello_Returns_200_HelloWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}

[Collection("Http2Integration")]
public sealed class Http2SmokeTests : IAsyncLifetime
{
    private readonly KestrelH2Fixture _fixture;
    private ClientHelper? _helper;

    public Http2SmokeTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_fixture.Port, new Version(2, 0));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_Hello_Returns_200_HelloWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}

[Collection("Http3Integration")]
[Trait("Category", "Http3")]
public sealed class Http3SmokeTests : IAsyncLifetime
{
    private readonly KestrelH3Fixture _fixture;
    private ClientHelper? _helper;

    public Http3SmokeTests(KestrelH3Fixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_fixture.Port, new Version(3, 0), scheme: "https");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Skip = "HTTP/3 QUIC transport not yet implemented — QuicClientProvider cannot establish connections")]
    public async Task Get_Hello_Returns_200_HelloWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}

[Collection("TlsIntegration")]
public sealed class TlsSmokeTests : IAsyncLifetime
{
    private readonly KestrelTlsFixture _fixture;
    private ClientHelper? _helper;

    public TlsSmokeTests(KestrelTlsFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_fixture.Port, new Version(1, 1), scheme: "https");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_Hello_Returns_200_HelloWorld()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");

        var response = await _helper!.Client.SendAsync(request, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal("Hello World", body);
    }
}
