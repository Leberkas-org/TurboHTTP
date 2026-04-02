using System.Text;

namespace TurboHttp.Protocol.Semantics;

/// <summary>
/// Represents a single authentication challenge from a <c>WWW-Authenticate</c> header.
/// RFC 9110 §11.6.1 — challenge = auth-scheme [ 1*SP ( token68 / #auth-param ) ]
/// </summary>
public sealed record AuthChallenge(string Scheme, IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Parses <c>WWW-Authenticate</c> header values into individual <see cref="AuthChallenge"/> instances.
/// RFC 9110 §11.6.1 — A server MAY generate a WWW-Authenticate header field with multiple challenges.
/// RFC 9110 §11.2 — Each parameter name MUST only occur once per challenge.
/// </summary>
public static class AuthChallengeParser
{
    /// <summary>
    /// Parses a <c>WWW-Authenticate</c> header value into one or more <see cref="AuthChallenge"/> instances.
    /// Supports multiple challenges separated by commas where schemes are detected by token position.
    /// </summary>
    /// <param name="wwwAuthenticate">The raw header value.</param>
    /// <returns>A list of parsed challenges.</returns>
    /// <exception cref="ArgumentException">Thrown when the header value is null or empty.</exception>
    /// <exception cref="FormatException">Thrown when duplicate parameter names are found within a challenge.</exception>
    public static IReadOnlyList<AuthChallenge> Parse(string wwwAuthenticate)
    {
        if (string.IsNullOrWhiteSpace(wwwAuthenticate))
        {
            throw new ArgumentException("WWW-Authenticate header value cannot be null or empty.", nameof(wwwAuthenticate));
        }

        var challenges = new List<AuthChallenge>();
        var span = wwwAuthenticate.AsSpan();
        var pos = 0;

        SkipWhitespace(span, ref pos);

        while (pos < span.Length)
        {
            var challenge = ParseChallenge(span, ref pos);
            challenges.Add(challenge);
            SkipWhitespace(span, ref pos);
        }

        return challenges;
    }

    private static AuthChallenge ParseChallenge(ReadOnlySpan<char> input, ref int pos)
    {
        var scheme = ReadToken(input, ref pos);
        if (scheme.Length == 0)
        {
            throw new FormatException($"Expected auth-scheme at position {pos}.");
        }

        SkipWhitespace(input, ref pos);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // If no more input or the next character starts a new challenge (no '='),
        // this is a scheme-only challenge (e.g., "Negotiate").
        if (pos >= input.Length)
        {
            return new AuthChallenge(scheme, parameters);
        }

        // Peek ahead to determine if we have auth-params or token68.
        // auth-param = token "=" ( token / quoted-string )
        // token68 = 1*( ALPHA / DIGIT / ... ) [ "=" ]
        // We check: is there a token followed by '=' followed by a value (with possible space)?
        // If the next token is followed by '=', it's parameters.
        var savedPos = pos;
        var firstToken = ReadToken(input, ref pos);

        if (pos < input.Length && input[pos] == '=')
        {
            // This is auth-param format: key=value
            pos++; // skip '='
            var value = ReadParamValue(input, ref pos);
            parameters.Add(firstToken, value);

            // Read remaining parameters
            while (pos < input.Length)
            {
                SkipWhitespace(input, ref pos);
                if (pos >= input.Length)
                {
                    break;
                }

                if (input[pos] == ',')
                {
                    pos++; // skip comma
                    SkipWhitespace(input, ref pos);

                    if (pos >= input.Length)
                    {
                        break;
                    }

                    // Peek to see if this is a new challenge (token without '=' after it,
                    // or a known scheme token followed by space+params or end).
                    var peekPos = pos;
                    var nextToken = ReadToken(input, ref peekPos);
                    SkipWhitespace(input, ref peekPos);

                    if (nextToken.Length > 0 && peekPos < input.Length && input[peekPos] == '=')
                    {
                        // It's another parameter for the current challenge
                        pos = peekPos + 1; // skip past '='
                        var paramValue = ReadParamValue(input, ref pos);

                        if (!parameters.TryAdd(nextToken, paramValue))
                        {
                            throw new FormatException(
                                $"Duplicate parameter name '{nextToken}' in {scheme} challenge. " +
                                "RFC 9110 §11.2 requires each parameter name to occur only once per challenge.");
                        }
                    }
                    else
                    {
                        // This is a new challenge scheme — don't advance pos, let outer loop handle it
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }
        else
        {
            // No '=' after first token — could be token68 or scheme-only.
            // For simplicity, treat as token68 value and store under empty key.
            // But more commonly this means the scheme has no params.
            // Reset position and return scheme-only.
            pos = savedPos;

            // Check if it looks like another scheme is following (for multi-challenge without params)
            // e.g., "Negotiate, Basic realm=..."
            // In that case the current scheme has no params.
        }

        return new AuthChallenge(scheme, parameters);
    }

    private static string ReadToken(ReadOnlySpan<char> input, ref int pos)
    {
        var start = pos;
        while (pos < input.Length && IsTokenChar(input[pos]))
        {
            pos++;
        }

        return input[start..pos].ToString();
    }

    private static string ReadParamValue(ReadOnlySpan<char> input, ref int pos)
    {
        if (pos < input.Length && input[pos] == '"')
        {
            return ReadQuotedString(input, ref pos);
        }

        return ReadToken(input, ref pos);
    }

    private static string ReadQuotedString(ReadOnlySpan<char> input, ref int pos)
    {
        pos++; // skip opening quote
        var sb = new StringBuilder();

        while (pos < input.Length)
        {
            if (input[pos] == '\\' && pos + 1 < input.Length)
            {
                pos++; // skip backslash
                sb.Append(input[pos]);
                pos++;
            }
            else if (input[pos] == '"')
            {
                pos++; // skip closing quote
                return sb.ToString();
            }
            else
            {
                sb.Append(input[pos]);
                pos++;
            }
        }

        // Unterminated quoted-string — return what we have
        return sb.ToString();
    }

    private static bool IsTokenChar(char c)
    {
        // RFC 9110 §5.6.2 — token = 1*tchar
        // tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." /
        //         "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
        return c switch
        {
            >= 'A' and <= 'Z' => true,
            >= 'a' and <= 'z' => true,
            >= '0' and <= '9' => true,
            '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' => true,
            '^' or '_' or '`' or '|' or '~' => true,
            _ => false
        };
    }

    private static void SkipWhitespace(ReadOnlySpan<char> input, ref int pos)
    {
        while (pos < input.Length && (input[pos] == ' ' || input[pos] == '\t'))
        {
            pos++;
        }
    }
}
