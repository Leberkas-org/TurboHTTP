using Akka;
using Akka.Streams.Dsl;

namespace TurboHTTP.Streams;

internal sealed class Engine
{
    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        TransportRegistry transports,
        PipelineDescriptor descriptor,
        TurboClientOptions? options = null,
        Func<TurboRequestOptions>? requestOptionsFactory = null)
    {
        options ??= new TurboClientOptions();
        requestOptionsFactory ??= () => BuildRequestOptions(options);

        var engineFlow = ProtocolCoreBuilder.Build(options, transports);

        return FeaturePipelineBuilder.Build(engineFlow, descriptor, requestOptionsFactory);
    }

    private static TurboRequestOptions BuildRequestOptions(TurboClientOptions options)
    {
        var holder = new HttpRequestMessage();
        return new TurboRequestOptions(
            BaseAddress: options.BaseAddress,
            DefaultRequestVersion: holder.Version,
            DefaultRequestHeaders: holder.Headers,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: options.Credentials,
            PreAuthenticate: options.PreAuthenticate);
    }
}
