using System.Net.Security;
using TurboHTTP.Server;

namespace TurboHTTP.Streams;

internal static class ProtocolRouter
{
    internal static IServerProtocolEngine ResolveEngine(SslApplicationProtocol protocol, TurboServerOptions options)
    {
        return protocol == SslApplicationProtocol.Http2
            ? new Http20ServerEngine(
                options.Http2.MaxConcurrentStreams,
                options.Http2.InitialConnectionWindowSize,
                options.Http2.InitialStreamWindowSize,
                options.Http2.MaxFrameSize,
                options.Http2.KeepAliveTimeout,
                options.Http2.RequestHeadersTimeout,
                options.Http2.MinRequestBodyDataRate,
                options.Http2.MinRequestBodyDataRateGracePeriod)
            : new Http11ServerEngine();
    }

    internal static IServerProtocolEngine ResolveEngine(Version version, TurboServerOptions options)
    {
        return version switch
        {
            { Major: 1, Minor: 0 } => new Http10ServerEngine(),
            { Major: 1, Minor: 1 } => new Http11ServerEngine(),
            { Major: 2, Minor: 0 } => new Http20ServerEngine(
                options.Http2.MaxConcurrentStreams,
                options.Http2.InitialConnectionWindowSize,
                options.Http2.InitialStreamWindowSize,
                options.Http2.MaxFrameSize,
                options.Http2.KeepAliveTimeout,
                options.Http2.RequestHeadersTimeout,
                options.Http2.MinRequestBodyDataRate,
                options.Http2.MinRequestBodyDataRateGracePeriod),
            { Major: 3, Minor: 0 } => new Http30ServerEngine(
                options.Http3.MaxRequestBodySize,
                options.Http3.KeepAliveTimeout,
                options.Http3.RequestHeadersTimeout,
                options.Http3.MinRequestBodyDataRate,
                options.Http3.MinRequestBodyDataRateGracePeriod),
            _ => new Http11ServerEngine()
        };
    }
}
