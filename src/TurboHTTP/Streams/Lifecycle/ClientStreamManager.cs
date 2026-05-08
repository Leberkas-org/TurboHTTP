using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;

namespace TurboHTTP.Streams.Lifecycle;

/// <summary>
/// Actor-based manager that supervises per-name <see cref="ClientStreamOwner"/> instances.
/// Clients register via Tell messages, sharing a single owned Owner per unique name.
/// On Dispose, the factory tells the manager to shutdown all children.
/// </summary>
internal sealed class ClientStreamManager : ReceiveActor
{
    internal sealed record RegisterConsumer(
        string Name,
        Guid ConsumerId,
        ChannelReader<HttpRequestMessage> RequestReader,
        ChannelWriter<HttpResponseMessage> ResponseWriter,
        Func<TurboRequestOptions> OptionsFactory,
        TurboClientOptions ClientOptions,
        PipelineDescriptor Pipeline);

    internal sealed record UnregisterConsumer(string Name, Guid ConsumerId);

    internal sealed record Shutdown;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Dictionary<string, OwnerState> _owners = [];

    public static Props Props()
    {
        return Akka.Actor.Props.Create(() => new ClientStreamManager());
    }

    public ClientStreamManager()
    {
        Receive<RegisterConsumer>(HandleRegisterConsumer);
        Receive<UnregisterConsumer>(HandleUnregisterConsumer);
        Receive<Shutdown>(_ => HandleShutdown());
    }

    private void HandleRegisterConsumer(RegisterConsumer message)
    {
        if (!_owners.TryGetValue(message.Name, out var state))
        {
            var sanitizedName = SanitizeActorName(message.Name);
            var owner = Context.ActorOf(
                Akka.Actor.Props.Create(() => new ClientStreamOwner(
                    message.ClientOptions,
                    message.Pipeline)),
                sanitizedName);

            var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>(
                new UnboundedChannelOptions { SingleReader = true });
            var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>(
                new UnboundedChannelOptions { SingleWriter = true });

            state = new OwnerState(owner, requestChannel, responseChannel);
            _owners[message.Name] = state;
        }

        state.Owner.Tell(new ClientStreamOwner.RegisterConsumer(
            message.ConsumerId,
            message.RequestReader,
            message.OptionsFactory,
            message.ResponseWriter));
    }

    private void HandleUnregisterConsumer(UnregisterConsumer message)
    {
        if (_owners.TryGetValue(message.Name, out var state))
        {
            state.Owner.Tell(new ClientStreamOwner.UnregisterConsumer(message.ConsumerId));
        }
    }

    private void HandleShutdown()
    {
        foreach (var state in _owners.Values)
        {
            state.RequestChannel.Writer.TryComplete();
            state.ResponseChannel.Writer.TryComplete();
            state.Owner.Tell(new ClientStreamOwner.Shutdown());
        }

        _owners.Clear();
        Context.Stop(Self);
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(ex =>
        {
            _log.Warning("ClientStreamOwner failed, restarting: {0}", ex.Message);
            return Directive.Restart;
        });
    }

    private static string SanitizeActorName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "default";
        }

        return Uri.EscapeDataString(name).Replace("%", "_");
    }

    private sealed record OwnerState(
        IActorRef Owner,
        Channel<HttpRequestMessage> RequestChannel,
        Channel<HttpResponseMessage> ResponseChannel);
}

internal sealed class NamedClientConsumerRegistration : IDisposable
{
    private readonly IActorRef _manager;
    private readonly string _name;
    private int _disposed;

    internal NamedClientConsumerRegistration(IActorRef manager, string name, Guid consumerId)
    {
        _manager = manager;
        _name = name;
        ConsumerId = consumerId;
    }

    internal Guid ConsumerId { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _manager.Tell(new ClientStreamManager.UnregisterConsumer(_name, ConsumerId));
    }
}
