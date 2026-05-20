using Servus.Akka.Transport;

namespace TurboHTTP.Server;

public sealed class ListenerBinding
{
    public required ListenerOptions Options { get; init; }
    public required IListenerFactory Factory { get; init; }
    public string? ConnectionLoggingCategory { get; init; }
}