using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboEntityMethodBuilder
{
    private Func<TurboHttpContext, IServiceProvider, ValueTask<object>> MessageFactory { get; }
    private bool _isTell;
    private TimeSpan? _timeoutOverride;
    private EntityResponseMapperCollection? _endpointMappers;
    private Func<TurboHttpContext, Task>? _tellResponseHandler;

    internal TurboEntityMethodBuilder(Func<TurboHttpContext, IServiceProvider, ValueTask<object>> messageFactory)
    {
        MessageFactory = messageFactory;
    }

    public void Ask(Action<TurboEntityAskBuilder> configure)
    {
        _isTell = false;
        _endpointMappers = null;
        _tellResponseHandler = null;

        var builder = new TurboEntityAskBuilder();
        configure(builder);

        if (builder.Mappers.Count == 0)
        {
            throw new InvalidOperationException(
                "IsAsk requires at least one Response<T> or Produces<T> handler.");
        }

        _endpointMappers = builder.Mappers;
        _timeoutOverride = builder.TimeoutOverride ?? _timeoutOverride;
    }

    public void Tell(Action<TurboEntityTellBuilder>? configure = null)
    {
        _isTell = true;
        _endpointMappers = null;

        if (configure is not null)
        {
            var builder = new TurboEntityTellBuilder();
            configure(builder);
            _tellResponseHandler = builder.ResponseHandler;
        }
    }

    [Obsolete("Use .IsTell() instead")]
    public TurboEntityMethodBuilder AcceptedResponse()
    {
        Tell();
        return this;
    }

    public TurboEntityMethodBuilder WithTimeout(TimeSpan timeout)
    {
        _timeoutOverride = timeout;
        return this;
    }

    internal EntityMethodConfig ToConfig() => new(
        MessageFactory,
        _isTell,
        _timeoutOverride,
        _endpointMappers,
        _tellResponseHandler);
}
