using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// RFC 9114 §4.2, §10.3 — Validates HTTP/3 field names and values.
///
/// HTTP/3 uses QPACK header compression which transmits field names as lowercase strings.
/// This validator enforces:
/// - Field names MUST be lowercase (uppercase characters are malformed)
/// - Field names MUST contain only valid token characters (RFC 9110 §5.1)
/// - Field values MUST NOT contain NUL (0x00), CR (0x0D), or LF (0x0A)
/// - Connection-specific headers are forbidden (Connection, Transfer-Encoding, Upgrade,
///   Proxy-Connection, Keep-Alive)
/// - The TE header is only allowed with value "trailers"
///
/// §10.3 — Intermediary encapsulation attack prevention:
/// An intermediary converting from HTTP/1.x to HTTP/3 MUST reject field names
/// that contain characters not valid in HTTP/3 to prevent request smuggling.
///
/// These rules apply to both request and response header fields.
/// </summary>
public static class Http3FieldValidator
{
    // RFC 9110 §5.1 token characters (excluding uppercase A-Z which are separately rejected):
    // token = 1*tchar
    // tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." /
    //         "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
    // In HTTP/3, ALPHA is lowercase only (a-z).
    private static readonly bool[] IsTokenChar = CreateTokenCharTable();

    private static bool[] CreateTokenCharTable()
    {
        var table = new bool[128];

        // DIGIT: 0-9
        for (var c = '0'; c <= '9'; c++)
        {
            table[c] = true;
        }

        // Lowercase ALPHA: a-z (uppercase is invalid in HTTP/3)
        for (var c = 'a'; c <= 'z'; c++)
        {
            table[c] = true;
        }

        // Special tchar characters
        foreach (var c in "!#$%&'*+-.^_`|~")
        {
            table[c] = true;
        }

        return table;
    }

    /// <summary>
    /// Validates all field names and values in the header list.
    /// Throws <see cref="Http3Exception"/> with <see cref="Http3ErrorCode.MessageError"/>
    /// if any field violates RFC 9114 §4.2 or §10.3 rules.
    /// </summary>
    /// <param name="headers">The header field list to validate.</param>
    public static void Validate(IReadOnlyList<(string Name, string Value)> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];

            // Skip pseudo-headers — validated separately by pseudo-header validators
            if (name.Length > 0 && name[0] == ':')
            {
                continue;
            }

            ValidateFieldName(name);
            ValidateFieldValue(name, value);
            ValidateConnectionSpecific(name, value);
        }
    }

    /// <summary>
    /// Validates that a field name contains only valid HTTP/3 token characters.
    /// RFC 9114 §4.2: No uppercase ASCII characters allowed.
    /// RFC 9114 §10.3: Field names containing characters not valid as a token
    /// (RFC 9110 §5.1) MUST be rejected to prevent intermediary encapsulation attacks.
    /// </summary>
    internal static void ValidateFieldName(string name)
    {
        if (name.Length == 0)
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §10.3: Empty field name is not a valid token");
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            // Check uppercase first (§4.2 — specific error message)
            if (c >= 'A' && c <= 'Z')
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    $"RFC 9114 §4.2: Field name '{name}' contains uppercase character '{c}' at position {i}");
            }

            // Check token validity (§10.3 — intermediary encapsulation prevention)
            if (c >= 128 || !IsTokenChar[c])
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    $"RFC 9114 §10.3: Field name '{name}' contains invalid character 0x{(int)c:X2} at position {i}");
            }
        }
    }

    /// <summary>
    /// Validates that a field value does not contain characters forbidden in HTTP/3.
    /// RFC 9114 §10.3: Field values containing NUL (0x00), CR (0x0D), or LF (0x0A)
    /// MUST be rejected to prevent intermediary encapsulation attacks.
    /// These characters could be used for response splitting or header injection
    /// when an intermediary translates between HTTP versions.
    /// </summary>
    internal static void ValidateFieldValue(string name, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\0')
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    $"RFC 9114 §10.3: Field '{name}' value contains NUL (0x00) at position {i}");
            }

            if (c == '\r')
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    $"RFC 9114 §10.3: Field '{name}' value contains CR (0x0D) at position {i}");
            }

            if (c == '\n')
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    $"RFC 9114 §10.3: Field '{name}' value contains LF (0x0A) at position {i}");
            }
        }
    }

    /// <summary>
    /// Validates that the field is not a connection-specific header forbidden in HTTP/3.
    /// RFC 9114 §4.2: "An intermediary transforming an HTTP/1.x message to HTTP/3
    /// MUST remove connection-specific header fields."
    ///
    /// The TE header is a special case: it is allowed only with the value "trailers"
    /// (RFC 9114 §4.2, RFC 9110 §7.6.1).
    /// </summary>
    internal static void ValidateConnectionSpecific(string name, string value)
    {
        if (string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Connection header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Transfer-Encoding header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "upgrade", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Upgrade header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "proxy-connection", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Proxy-Connection header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "keep-alive", StringComparison.OrdinalIgnoreCase))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.2: Keep-Alive header is forbidden in HTTP/3");
        }

        if (string.Equals(name, "te", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(value, "trailers", StringComparison.OrdinalIgnoreCase))
            {
                throw new Http3Exception(Http3ErrorCode.MessageError,
                    $"RFC 9114 §4.2: TE header is only allowed with value 'trailers', got '{value}'");
            }
        }
    }
}
