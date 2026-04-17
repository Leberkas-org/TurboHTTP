using TurboHTTP.Protocol.Caching;
using TurboHTTP.Protocol.Cookies;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP;

internal sealed class TurboClientDescriptor
{
    public RedirectPolicy? RedirectPolicy { get; set; }
    public RetryPolicy? RetryPolicy { get; set; }
    public Expect100Policy? Expect100Policy { get; set; }
    public bool AutomaticDecompression { get; set; } = true;
    public CompressionPolicy? CompressionPolicy { get; set; }
    public bool EnableCookies { get; set; }
    public CookieJar? CustomCookieJar { get; set; }
    public CachePolicy? CachePolicy { get; set; }
    public CacheStore? CustomCacheStore { get; set; }
    public List<Type> HandlerTypes { get; } = [];

    /// <summary>
    /// Unified FIFO factory list covering both type-based and delegate-based handlers.
    /// Type-based registrations (AddHandler&lt;T&gt;) add to both HandlerTypes and this list.
    /// Delegate-based registrations (UseRequest/UseResponse) add only to this list.
    /// </summary>
    public List<Func<IServiceProvider, TurboHandler>> HandlerFactories { get; } = [];
}