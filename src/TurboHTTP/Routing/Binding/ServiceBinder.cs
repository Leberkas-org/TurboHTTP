using TurboHTTP.Server;

namespace TurboHTTP.Routing.Binding;

internal sealed class ServiceBinder(Type serviceType) : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
        => ValueTask.FromResult(services.GetService(serviceType));
}