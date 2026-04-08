using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;

namespace TurboHTTP.StreamTests.Streams;

/// <summary>
/// Test-only adapter that bridges a <see cref="Func{TResult}"/> delegate to the
/// <see cref="ITransportFactory"/> interface, allowing existing test code to register
/// transport flows without creating named factory classes.
/// </summary>
internal sealed class DelegateTransportFactory(Func<Flow<IOutputItem, IInputItem, NotUsed>> factory)
    : ITransportFactory
{
    public Flow<IOutputItem, IInputItem, NotUsed> Create() => factory();
}
