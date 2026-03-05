using System;

namespace TurboHttp.Protocol;

/// <summary>
/// Error codes for redirect failures per RFC 9110 §15.4.
/// </summary>
public enum RedirectError
{
    /// <summary>The maximum number of redirects was exceeded.</summary>
    MaxRedirectsExceeded,

    /// <summary>A redirect loop was detected (a URI appeared twice in the redirect chain).</summary>
    RedirectLoop,

    /// <summary>The redirect response is missing the required Location header.</summary>
    MissingLocationHeader,

    /// <summary>The Location header value is not a valid URI.</summary>
    InvalidLocationHeader,
}

/// <summary>
/// Thrown when an RFC 9110 §15.4 redirect constraint is violated.
/// </summary>
public sealed class RedirectException : Exception
{
    /// <summary>The specific redirect error that occurred.</summary>
    public RedirectError Error { get; }

    /// <inheritdoc />
    public RedirectException(string message, RedirectError error)
        : base(message)
    {
        Error = error;
    }

    /// <inheritdoc />
    public RedirectException(string message, RedirectError error, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }
}

/// <summary>
/// Thrown when a redirect would downgrade from HTTPS to HTTP and the policy forbids it.
/// </summary>
public sealed class RedirectDowngradeException : Exception
{
    /// <inheritdoc />
    public RedirectDowngradeException(string message) : base(message) { }
}
