using System.Collections.Generic;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Streams;

internal sealed record PipelineDescriptor(
    RedirectPolicy? RedirectPolicy,
    RetryPolicy? RetryPolicy,
    CookieJar? CookieJar,
    HttpCacheStore? CacheStore,
    CachePolicy? CachePolicy,
    IReadOnlyList<TurboHandler> Middlewares,
    bool AutomaticDecompression = true)
{
    public static readonly PipelineDescriptor Empty = new(
        RedirectPolicy: null,
        RetryPolicy: null,
        CookieJar: null,
        CacheStore: null,
        CachePolicy: null,
        Middlewares: [],
        AutomaticDecompression: true);
}
