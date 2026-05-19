using TurboHTTP.Client;
using System.Net;
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

    protected async Task<HttpResponseMessage> SendClientH2Async(
        HttpRequestMessage request,
        byte[] serverFrames,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateH2Connection(new[] { serverFrames });
        var transports = new TransportRegistry()
            .Register(HttpVersion.Version20, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, HttpVersion.Version20, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    protected async Task<HttpResponseMessage> SendClientH3Async(
        HttpRequestMessage request,
        byte[][] serverFrames,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateH3Connection(serverFrames);
        var transports = new TransportRegistry()
            .Register(HttpVersion.Version30, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, HttpVersion.Version30, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    protected async Task<List<HttpResponseMessage>> SendClientManyAsync(
        Version version,
        IReadOnlyList<HttpRequestMessage> requests,
        Func<int, byte[], byte[]?> responseFactory,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateScriptedConnection(responseFactory);
        var transports = new TransportRegistry()
            .Register(version, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, version, configure, configureOptions);

        var responses = new List<HttpResponseMessage>();
        foreach (var request in requests)
        {
            var response = await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
            responses.Add(response);
        }

        return responses;
    }
}