using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Streams;

internal sealed class Engine
{
    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        TransportRegistry transports,
        PipelineDescriptor descriptor,
        TurboClientOptions? options = null)
    {
        options ??= new TurboClientOptions();

        var engineFlow = ProtocolCoreBuilder.Build(options, transports);

        return FeaturePipelineBuilder.Build(engineFlow, descriptor);
    }
}
