using Akka.TestKit.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHTTP.Client;

namespace TurboHTTP.Tests.Client;

public sealed class NamedClientRuntimeSpec : TestKit
{
    [Fact(Timeout = 15000)]
    public void CreateClient_same_name_should_reuse_single_named_runtime()
    {
        const string name = "shared-runtime";
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<TurboClientOptions>(name, _ => { });
        services.Configure<TurboClientDescriptor>(name, _ => { });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();

        using var factory = new TurboHttpClientFactory(options, descriptors, provider, Sys);
        using var first = (TurboHttpClient)factory.CreateClient(name);
        using var second = (TurboHttpClient)factory.CreateClient(name);

        Assert.NotEqual(first.ConsumerId, second.ConsumerId);
        Assert.NotSame(first.Requests, second.Requests);
        Assert.NotSame(first.Responses, second.Responses);
    }

    [Fact(Timeout = 30000)]
    public async Task CreateClient_concurrent_same_name_should_reuse_single_named_runtime()
    {
        const string name = "shared-runtime-concurrent";
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<TurboClientOptions>(name, _ => { });
        services.Configure<TurboClientDescriptor>(name, _ => { });

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();

        using var factory = new TurboHttpClientFactory(options, descriptors, provider, Sys);
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => (TurboHttpClient)factory.CreateClient(name),
                TestContext.Current.CancellationToken))
            .ToArray();
        var clients = await Task.WhenAll(tasks);

        try
        {
            Assert.Equal(clients.Length, clients.Select(c => c.ConsumerId).Distinct().Count());
            Assert.Equal(clients.Length, clients.Select(c => c.Requests).Distinct().Count());
            Assert.Equal(clients.Length, clients.Select(c => c.Responses).Distinct().Count());
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact(Timeout = 15000)]
    public void CreateClient_different_names_should_not_share_named_runtime()
    {
        const string firstName = "runtime-a";
        const string secondName = "runtime-b";
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<TurboClientOptions>(firstName, _ => { });
        services.Configure<TurboClientDescriptor>(firstName, _ => { });
        services.Configure<TurboClientOptions>(secondName, _ => { });
        services.Configure<TurboClientDescriptor>(secondName, _ => { });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();

        using var factory = new TurboHttpClientFactory(options, descriptors, provider, Sys);
        using var first = (TurboHttpClient)factory.CreateClient(firstName);
        using var second = (TurboHttpClient)factory.CreateClient(secondName);

        Assert.NotSame(first.Requests, second.Requests);
        Assert.NotSame(first.Responses, second.Responses);
    }

    [Fact(Timeout = 15000)]
    public void CreateClient_shared_runtime_should_apply_named_base_address_defaults()
    {
        const string name = "named-defaults";
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<TurboClientOptions>(name,
            options => { options.BaseAddress = new Uri("https://named.example"); });
        services.Configure<TurboClientDescriptor>(name, _ => { });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();

        using var factory = new TurboHttpClientFactory(options, descriptors, provider, Sys);
        using var client = (TurboHttpClient)factory.CreateClient(name);

        Assert.Equal(new Uri("https://named.example"), client.BaseAddress);
    }

    [Fact(Timeout = 15000)]
    public void CreateClient_should_create_named_consumer_registration()
    {
        const string name = "registration-test";
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<TurboClientOptions>(name, _ => { });
        services.Configure<TurboClientDescriptor>(name, _ => { });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();

        using var factory = new TurboHttpClientFactory(options, descriptors, provider, Sys);
        using var client = (TurboHttpClient)factory.CreateClient(name);

        Assert.NotEqual(Guid.Empty, client.ConsumerId);
    }
}
