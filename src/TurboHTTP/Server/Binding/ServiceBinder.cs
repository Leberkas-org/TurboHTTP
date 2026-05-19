namespace TurboHTTP.Server.Binding;

internal sealed class ServiceBinder(Type serviceType) : ParameterBinder
{
    public override ValueTask<object?> BindAsync(TurboHttpContext ctx, IServiceProvider services)
        => ValueTask.FromResult(services.GetService(serviceType));
}