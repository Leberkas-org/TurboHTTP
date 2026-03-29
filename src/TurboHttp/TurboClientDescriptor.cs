using Akka.Actor;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp;

internal sealed class TurboClientDescriptor
{
    public RedirectPolicy? RedirectPolicy { get; set; }
    public RetryPolicy? RetryPolicy { get; set; }
    public Expect100Policy? Expect100Policy { get; set; }
    public RequestCompressionPolicy? RequestCompressionPolicy { get; set; }
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
    public bool AutomaticDecompression { get; set; } = true;

    /// <summary>
    /// Optional custom supervisor strategy for the <c>ClientStreamOwner</c> actor.
    /// When <see langword="null"/>, the default <c>AllForOneStrategy</c> with 3 retries
    /// and exponential backoff (100ms, 500ms, 2s) is used.
    /// </summary>
    public SupervisorStrategy? CustomSupervisorStrategy { get; set; }
}
