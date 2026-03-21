using System.Collections.Generic;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Streams;

internal sealed record PipelineDescriptor(
    RedirectPolicy? RedirectPolicy,
    RetryPolicy? RetryPolicy,
    Expect100Policy? Expect100Policy,
    CookieJar? CookieJar,
    HttpCacheStore? CacheStore,
    CachePolicy? CachePolicy,
    IReadOnlyList<TurboHandler> Handlers,
    bool AutomaticDecompression = true)
{
    public static readonly PipelineDescriptor Empty = new(
        RedirectPolicy: null,
        RetryPolicy: null,
        Expect100Policy: null,
        CookieJar: null,
        CacheStore: null,
        CachePolicy: null,
        Handlers: [],
        AutomaticDecompression: true);
}
