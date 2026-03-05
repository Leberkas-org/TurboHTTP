namespace TurboHttp.Protocol;

/// <summary>
/// Configuration for redirect behavior in <see cref="RedirectHandler"/>.
/// </summary>
public sealed record RedirectPolicy
{
    /// <summary>Default redirect policy: max 10 redirects, no HTTPS→HTTP downgrade.</summary>
    public static readonly RedirectPolicy Default = new();

    /// <summary>
    /// Maximum number of redirects to follow before throwing <see cref="RedirectException"/>.
    /// Default is 10.
    /// </summary>
    public int MaxRedirects { get; init; } = 10;

    /// <summary>
    /// If true, allows redirects from HTTPS to HTTP.
    /// Default is false (downgrade blocked by default for security).
    /// </summary>
    public bool AllowHttpsToHttpDowngrade { get; init; } = false;
}
