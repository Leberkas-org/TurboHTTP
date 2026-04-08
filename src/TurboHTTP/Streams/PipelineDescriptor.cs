using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Caching;

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
    bool AutomaticDecompression = true)
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
        AutomaticDecompression: true);
}