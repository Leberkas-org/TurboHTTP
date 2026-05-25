using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Routing;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public sealed class TurboWebApplication : IHost, ITurboEndpointRouteBuilder, ITurboPipelineBuilder, IAsyncDisposable
{
    private readonly IHost _host;
    private readonly TurboRouteTable _routeTable;
    private readonly TurboPipelineBuilder _pipelineBuilder;
    private readonly TurboUrlCollection _urls;

    internal TurboWebApplication(IHost host, TurboRouteTable routeTable, TurboPipelineBuilder pipelineBuilder)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _routeTable = routeTable ?? throw new ArgumentNullException(nameof(routeTable));
        _pipelineBuilder = pipelineBuilder ?? throw new ArgumentNullException(nameof(pipelineBuilder));

        var options = host.Services.GetRequiredService<TurboServerOptions>();
        _urls = new TurboUrlCollection(options);

        Logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(Environment.ApplicationName ?? nameof(TurboWebApplication));
    }

    public IServiceProvider Services => _host.Services;

    public IConfiguration Configuration => Services.GetRequiredService<IConfiguration>();

    public IHostEnvironment Environment => Services.GetRequiredService<IHostEnvironment>();

    public IHostApplicationLifetime Lifetime => Services.GetRequiredService<IHostApplicationLifetime>();

    public ILogger Logger { get; }

    public ICollection<string> Urls => _urls;

    public void Dispose()
    {
        _host.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _host.Dispose();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _host.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _host.StopAsync(cancellationToken);
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return _host.RunAsync(cancellationToken);
    }

    public async Task RunAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await _host.RunAsync(cts.Token);
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
    {
        return _host.WaitForShutdownAsync(cancellationToken);
    }

    IServiceProvider ITurboEndpointRouteBuilder.ServiceProvider => Services;

    TurboRouteTable ITurboEndpointRouteBuilder.RouteTable => _routeTable;

    public ITurboPipelineBuilder Use(Func<TurboHttpContext, TurboRequestDelegate, Task> middleware)
    {
        _pipelineBuilder.Use(middleware);
        return this;
    }

    public ITurboPipelineBuilder Use<T>() where T : class, ITurboMiddleware
    {
        _pipelineBuilder.Use<T>();
        return this;
    }

    public ITurboPipelineBuilder Run(TurboRequestDelegate handler)
    {
        _pipelineBuilder.Run(handler);
        return this;
    }

    public ITurboPipelineBuilder Map(string pathPrefix, Action<ITurboPipelineBuilder> configure)
    {
        _pipelineBuilder.Map(pathPrefix, configure);
        return this;
    }

    public ITurboPipelineBuilder MapWhen(Func<TurboHttpContext, bool> predicate, Action<ITurboPipelineBuilder> configure)
    {
        _pipelineBuilder.MapWhen(predicate, configure);
        return this;
    }

    public static TurboWebApplicationBuilder CreateBuilder()
    {
        return new TurboWebApplicationBuilder(null);
    }

    public static TurboWebApplicationBuilder CreateBuilder(string[] args)
    {
        return new TurboWebApplicationBuilder(args);
    }

    public static TurboWebApplication Create(string[]? args = null)
    {
        return new TurboWebApplicationBuilder(args).Build();
    }
}
