namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// RFC 9114 §10.3 — Validates request origins for intermediary encapsulation attack prevention.
///
/// An intermediary that translates an HTTP/1.x request to HTTP/3 MUST reject requests
/// targeting origins that contain features that cannot be safely represented in HTTP/3.
/// Specifically:
/// - Userinfo in the authority component (user:password@host) is forbidden
/// - Empty scheme is forbidden (HTTP/3 always requires :scheme)
/// - Empty path for non-CONNECT requests is forbidden (:path must not be empty)
/// - Fragment identifiers in :path are forbidden (RFC 9110 §7.1)
///
/// These checks protect against request smuggling where an intermediary might
/// forward requests that a downstream HTTP/3 server interprets differently.
/// </summary>
public static class Http3OriginValidator
{
    /// <summary>
    /// Validates that a request URI does not target a prohibited origin.
    /// Called during request encoding to prevent intermediary encapsulation attacks.
    /// </summary>
    /// <param name="uri">The request URI to validate.</param>
    /// <param name="isConnect">True if this is a CONNECT request (relaxed path requirements).</param>
    public static void Validate(Uri uri, bool isConnect = false)
    {
        ArgumentNullException.ThrowIfNull(uri);

        ValidateNoUserInfo(uri);

        if (!isConnect)
        {
            ValidateScheme(uri);
            ValidatePath(uri);
        }
    }

    /// <summary>
    /// RFC 9114 §10.3: Reject URIs containing userinfo (user:password@host).
    /// HTTP/3 pseudo-header :authority MUST NOT contain userinfo.
    /// An intermediary forwarding such a request would leak credentials.
    /// </summary>
    internal static void ValidateNoUserInfo(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §10.3: Request URI contains userinfo which cannot be represented in HTTP/3 :authority");
        }
    }

    /// <summary>
    /// RFC 9114 §10.3: Reject requests with empty or missing scheme.
    /// HTTP/3 requires the :scheme pseudo-header for non-CONNECT requests.
    /// </summary>
    internal static void ValidateScheme(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Scheme))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §10.3: Request URI has empty scheme which cannot be represented in HTTP/3 :scheme");
        }
    }

    /// <summary>
    /// RFC 9114 §10.3: Reject requests with empty path for non-CONNECT methods.
    /// HTTP/3 requires the :path pseudo-header to be non-empty for non-CONNECT requests.
    /// Also rejects paths containing fragment identifiers (RFC 9110 §7.1).
    /// </summary>
    internal static void ValidatePath(Uri uri)
    {
        var path = uri.AbsolutePath;

        if (string.IsNullOrEmpty(path))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §10.3: Request URI has empty path which cannot be represented in HTTP/3 :path");
        }

        // Fragment identifiers MUST NOT be sent in :path (RFC 9110 §7.1)
        if (uri.Fragment.Length > 0)
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §10.3: Request URI contains fragment identifier which MUST NOT appear in HTTP/3 :path");
        }
    }
}
