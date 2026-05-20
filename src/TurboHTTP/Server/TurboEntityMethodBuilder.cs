using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboEntityMethodBuilder
{
    private Func<TurboHttpContext, IServiceProvider, ValueTask<object>> MessageFactory { get; }
    private bool IsTell { get; set; }
    private TimeSpan? TimeoutOverride { get; set; }

    internal TurboEntityMethodBuilder(Func<TurboHttpContext, IServiceProvider, ValueTask<object>> messageFactory)
    {
        MessageFactory = messageFactory;
    }

    public TurboEntityMethodBuilder AcceptedResponse()
    {
        IsTell = true;
        return this;
    }

    public TurboEntityMethodBuilder WithTimeout(TimeSpan timeout)
    {
        TimeoutOverride = timeout;
        return this;
    }

    internal EntityMethodConfig ToConfig() => new(MessageFactory, IsTell, TimeoutOverride, null, null);
}
