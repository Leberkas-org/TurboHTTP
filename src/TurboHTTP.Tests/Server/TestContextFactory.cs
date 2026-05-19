using System.Net;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server;

internal static class TestContextFactory
{
    public static TurboHttpContext Create(
        HttpRequestMessage? request = null,
        TurboConnectionInfo? connection = null,
        IServiceProvider? services = null,
        CancellationToken cancellationToken = default)
    {
        var req = request ?? new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        var conn = connection ?? new TurboConnectionInfo("test", IPAddress.Loopback, 0, IPAddress.Loopback, 0);

        var features = new FeatureCollection();
        var requestFeature = new TurboHttpRequestFeature(req, Source.Empty<ReadOnlyMemory<byte>>());
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<ITurboRequestBodyFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        features.Set<IHttpConnectionFeature>(new TurboHttpConnectionFeature(conn));
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<ITurboResponseBodyFeature>(bodyFeature);

        return new TurboHttpContext(features, conn, services, cancellationToken, null!);
    }

    public static TurboHttpContext Create(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        return Create(request: request);
    }
}