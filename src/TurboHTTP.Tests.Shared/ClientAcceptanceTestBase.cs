using TurboHTTP.Client;
using TurboHTTP.Streams;
using Xunit;

namespace TurboHTTP.Tests.Shared;

public abstract class ClientAcceptanceTestBase : AcceptanceTestBase
{
    protected async Task<HttpResponseMessage> SendClientAsync(
        Version version,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateScriptedConnection(responseFactory);
        var transports = new TransportRegistry()
            .Register(version, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, version, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}