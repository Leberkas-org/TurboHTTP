using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Client;

public sealed class RedirectOptions
{
    /// <summary>
    /// Maximum number of redirects to follow before throwing <see cref="RedirectException"/>.
    /// Default is 10.
    /// </summary>
    public int MaxRedirects { get; set; } = 10;

    /// <summary>
    /// If true, allows redirects from HTTPS to HTTP.
    /// Default is false (downgrade blocked by default for security).
    /// </summary>
    public bool AllowHttpsToHttpDowngrade { get; set; }

    internal RedirectPolicy To() => new()
    {
        MaxRedirects = MaxRedirects,
        AllowHttpsToHttpDowngrade = AllowHttpsToHttpDowngrade
    };
}