using TurboHTTP.Protocol.AltSvc;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Streams;

internal sealed record PipelineDescriptor(
    RedirectPolicy? RedirectPolicy,
    RetryPolicy? RetryPolicy,
    Expect100Policy? Expect100Policy,
    CompressionPolicy? CompressionPolicy,
    CookieJar? CookieJar,
    CacheStore? CacheStore,
    CachePolicy? CachePolicy,
    IReadOnlyList<TurboHandler> Handlers,
    bool AutomaticDecompression = true,
    AltSvcCache? AltSvcCache = null)
{
    public static readonly PipelineDescriptor Empty = new(
        RedirectPolicy: null,
        RetryPolicy: null,
        Expect100Policy: null,
        CompressionPolicy: null,
        CookieJar: null,
        CacheStore: null,
        CachePolicy: null,
        Handlers: [],
        AutomaticDecompression: true,
        AltSvcCache: null);
}