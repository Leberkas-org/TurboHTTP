using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace TurboHTTP.Server;

public static class TurboServerWebHostBuilderExtensions
{
    public static IHostBuilder UseTurboHttp(
        this IHostBuilder builder,
        Action<TurboServerOptions>? configure = null)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServer>();
            services.AddSingleton<IServer, TurboServer>();
            if (configure is not null)
            {
                services.Configure(configure);
            }
        });
        return builder;
    }
}
