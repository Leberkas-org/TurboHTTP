using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHTTP.Client;

namespace TurboHTTP.Tests.Client;

public sealed class TurboClientServiceCollectionExtensionsSpec
{
    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_WithName_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddTurboHttpClient("test");

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<ITurboHttpClientBuilder>(builder);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_WithName_RegistersOptionsMonitor()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test");

        // Verify that builder was returned and configuration would work
        var sp = services.BuildServiceProvider();
        var monitor = sp.GetService<IOptionsMonitor<TurboClientOptions>>();
        Assert.NotNull(monitor);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_WithNameAndConfigure_RegistersConfiguration()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test", opt => opt.ConnectTimeout = TimeSpan.FromSeconds(30));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get("test");

        Assert.Equal(TimeSpan.FromSeconds(30), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_DefaultName_UsesEmptyString()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient();

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get(string.Empty);

        Assert.NotNull(options);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_MultipleClients_ConfiguresEachSeparately()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test1", opt => opt.ConnectTimeout = TimeSpan.FromSeconds(10));
        services.AddTurboHttpClient("test2", opt => opt.ConnectTimeout = TimeSpan.FromSeconds(20));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options1 = optionsMonitor.Get("test1");
        var options2 = optionsMonitor.Get("test2");

        Assert.Equal(TimeSpan.FromSeconds(10), options1.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(20), options2.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_WithoutConfigure_AllowsBuilderConfiguration()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test")
            .WithCookies();

        var sp = services.BuildServiceProvider();
        var descriptorMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();
        var descriptor = descriptorMonitor.Get("test");

        Assert.True(descriptor.EnableCookies);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_TypedClient_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddTurboHttpClient<TestClient>();

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<ITurboHttpClientBuilder>(builder);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_TypedClient_UsesTypeNameAsClientName()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient<TestClient>(opt => opt.ConnectTimeout = TimeSpan.FromSeconds(25));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get("TestClient");

        Assert.Equal(TimeSpan.FromSeconds(25), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_TypedClientInterface_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddTurboHttpClient<ITestClient, TestClientImpl>();

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<ITurboHttpClientBuilder>(builder);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_TypedClientInterface_UsesInterfaceNameAsClientName()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient<ITestClient, TestClientImpl>(opt => opt.ConnectTimeout = TimeSpan.FromSeconds(35));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get("ITestClient");

        Assert.Equal(TimeSpan.FromSeconds(35), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void CreateClient_WithNullFactory_ThrowsArgumentNullException()
    {
        ITurboHttpClientFactory? nullFactory = null;

        Assert.Throws<ArgumentNullException>(() => nullFactory!.CreateClient());
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_MultipleExtensions_AllConfigured()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("client1", opt => opt.MaxEndpointSubstreams = 100);
        services.AddTurboHttpClient("client2", opt => opt.MaxEndpointSubstreams = 200);

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options1 = optionsMonitor.Get("client1");
        var options2 = optionsMonitor.Get("client2");

        Assert.Equal(100u, options1.MaxEndpointSubstreams);
        Assert.Equal(200u, options2.MaxEndpointSubstreams);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_RegistersTurboHttpClientName()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test");

        var sp = services.BuildServiceProvider();
        var names = sp.GetServices<TurboHttpClientName>();

        Assert.Contains(names, n => n.Name == "test");
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpClient_WithDefaultName_RegistersEmptyName()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient();

        var sp = services.BuildServiceProvider();
        var names = sp.GetServices<TurboHttpClientName>();

        Assert.Contains(names, n => n.Name == string.Empty);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientName_RecordType_HasName()
    {
        var name = new TurboHttpClientName("test");

        Assert.Equal("test", name.Name);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpClientName_Equality_ComparesName()
    {
        var name1 = new TurboHttpClientName("test");
        var name2 = new TurboHttpClientName("test");
        var name3 = new TurboHttpClientName("other");

        Assert.Equal(name1, name2);
        Assert.NotEqual(name1, name3);
    }

    private sealed class TestClient;

    private interface ITestClient;

    private sealed class TestClientImpl : ITestClient;
}