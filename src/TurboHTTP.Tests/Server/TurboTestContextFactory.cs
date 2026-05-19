using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server;

internal static class TurboTestContextFactory
{
    internal static TurboHttpContext Create(
        string method = "GET",
        string uri = "http://localhost/test",
        string? path = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri);

        var features = new FeatureCollection();
        var requestFeature = new TurboHttpRequestFeature(request, Source.Empty<ReadOnlyMemory<byte>>());
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<ITurboRequestBodyFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);

        if (path is not null)
        {
            requestFeature.Path = path;
        }

        return new TurboHttpContext(
            features,
            new TurboConnectionInfo("test", null, 0, null, 0),
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None, null!);
    }
}