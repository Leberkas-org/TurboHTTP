using System.Text;

namespace TurboHttp.Protocol.Semantics;

/// <summary>
/// Builds <c>Authorization</c> header values for common authentication schemes.
/// RFC 9110 §11.6.2 — Authorization = credentials
/// RFC 9110 §11.2 — Senders MUST only generate quoted-string syntax for auth-param values.
/// </summary>
public static class AuthorizationBuilder
{
    /// <summary>
    /// Builds a Basic authorization header value.
    /// RFC 9110 §11.7.1 — credentials = "Basic" SP base64(user-id ":" password)
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <returns>The full <c>Basic {base64}</c> credentials string.</returns>
    public static string BuildBasic(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        var credentials = $"{username}:{password}";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
        return $"Basic {base64}";
    }

    /// <summary>
    /// Builds a Bearer authorization header value.
    /// RFC 6750 §2.1 — credentials = "Bearer" SP b64token
    /// </summary>
    /// <param name="token">The bearer token.</param>
    /// <returns>The full <c>Bearer {token}</c> credentials string.</returns>
    public static string BuildBearer(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Bearer token cannot be empty.", nameof(token));
        }

        return $"Bearer {token}";
    }

    /// <summary>
    /// Builds a custom authorization header value with quoted-string parameter values.
    /// RFC 9110 §11.2 — "A sender MUST only generate the quoted-string syntax for parameter values."
    /// </summary>
    /// <param name="scheme">The authentication scheme name.</param>
    /// <param name="parameters">The auth-param key-value pairs. Values will be quoted-string encoded.</param>
    /// <returns>The full credentials string with quoted parameter values.</returns>
    public static string BuildCustom(string scheme, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.Count == 0)
        {
            return scheme;
        }

        var sb = new StringBuilder(scheme);
        var first = true;

        foreach (var (key, value) in parameters)
        {
            if (first)
            {
                sb.Append(' ');
                first = false;
            }
            else
            {
                sb.Append(", ");
            }

            sb.Append(key);
            sb.Append('=');
            sb.Append(QuoteString(value));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Encodes a value as a quoted-string per RFC 9110 §5.6.4.
    /// quoted-string = DQUOTE *( qdtext / quoted-pair ) DQUOTE
    /// </summary>
    private static string QuoteString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');

        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        sb.Append('"');
        return sb.ToString();
    }
}
