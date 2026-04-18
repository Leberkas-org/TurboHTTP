using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;

namespace TurboHTTP.StreamTests.Streams;

internal sealed class DelegateTransportFactory(Func<Flow<IOutputItem, IInputItem, NotUsed>> factory)
    : ITransportFactory
{
    public Flow<IOutputItem, IInputItem, NotUsed> Create() => factory();
}