using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.IO;

namespace TurboHTTP.StreamTests.Streams;

internal sealed class DelegateTransportFactory(Func<Flow<IOutputItem, IInputItem, NotUsed>> factory)
    : ITransportFactory
{
    public Flow<IOutputItem, IInputItem, NotUsed> Create() => factory();
}