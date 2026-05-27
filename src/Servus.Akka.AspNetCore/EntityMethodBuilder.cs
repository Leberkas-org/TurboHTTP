using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore;

public sealed class EntityMethodBuilder
{
    private readonly Delegate _messageFactory;
    private bool _isTell;
    private TimeSpan? _timeoutOverride;
    private EntityResponseMapperCollection? _endpointMappers;
    private Func<HttpContext, Task>? _tellResponseHandler;

    internal EntityMethodBuilder(Delegate messageFactory)
    {
        _messageFactory = messageFactory;
    }

    public EntityMethodBuilder Ask(Action<EntityAskBuilder> configure)
    {
        _isTell = false;
        var builder = new EntityAskBuilder();
        configure(builder);
        _endpointMappers = builder.Mappers;
        _timeoutOverride = builder.TimeoutOverride ?? _timeoutOverride;
        return this;
    }

    public EntityMethodBuilder Tell(Action<EntityTellBuilder>? configure = null)
    {
        _isTell = true;
        if (configure is not null)
        {
            var builder = new EntityTellBuilder();
            configure(builder);
            _tellResponseHandler = builder.ResponseHandler;
        }
        return this;
    }

    public EntityMethodBuilder WithTimeout(TimeSpan timeout)
    {
        _timeoutOverride = timeout;
        return this;
    }

    internal EntityMethodConfig ToConfig()
    {
        return new EntityMethodConfig(
            _messageFactory,
            _isTell,
            _timeoutOverride,
            _endpointMappers,
            _tellResponseHandler);
    }
}
