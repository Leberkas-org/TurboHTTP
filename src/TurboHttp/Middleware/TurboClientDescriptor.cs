using System;
using System.Collections.Generic;
using TurboHttp.Protocol.RFC6265;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;

namespace TurboHttp.Middleware;

internal sealed class TurboClientDescriptor
{
    public RedirectPolicy? RedirectPolicy { get; set; }
    public RetryPolicy? RetryPolicy { get; set; }
    public bool EnableCookies { get; set; }
    public CookieJar? CustomCookieJar { get; set; }
    public CachePolicy? CachePolicy { get; set; }
    public List<Type> MiddlewareTypes { get; } = [];

    /// <summary>
    /// Unified FIFO factory list covering both type-based and delegate-based middleware.
    /// Type-based registrations (AddMiddleware&lt;T&gt;) add to both MiddlewareTypes and this list.
    /// Delegate-based registrations (UseRequest/UseResponse) add only to this list.
    /// </summary>
    public List<Func<IServiceProvider, TurboMiddleware>> MiddlewareFactories { get; } = [];
    public bool AutomaticDecompression { get; set; } = true;
}
