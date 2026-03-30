using System.Globalization;

namespace TurboHttp.Protocol.RFC9110;

/// <summary>
/// RFC 9110 §13.1.5 — validates the If-Range request header.
/// <list type="bullet">
///   <item>If-Range MUST NOT be sent without a Range header.</item>
///   <item>If-Range MUST NOT contain a weak entity-tag.</item>
///   <item>If-Range SHOULD use a strong entity-tag when one is available
///         (sending an HTTP-date when an ETag is available is treated as an error).</item>
/// </list>
/// </summary>
internal static class IfRangeValidator
{
    private static readonly string[] HttpDateFormats =
    [
        "r",                                        // RFC 1123
        "dddd, dd-MMM-yy HH:mm:ss 'GMT'",           // RFC 850
        "ddd MMM  d HH:mm:ss yyyy",                 // asctime
        "ddd MMM dd HH:mm:ss yyyy",                 // asctime (two-digit day)
    ];

    /// <summary>
    /// Validates the If-Range header on <paramref name="request"/>.
    /// Throws <see cref="InvalidOperationException"/> on RFC violations.
    /// </summary>
    public static void Validate(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("If-Range", out var ifRangeValues))
        {
            return;
        }

        // If-Range without Range is meaningless — RFC 9110 §13.1.5:
        // "A client MUST NOT generate an If-Range header field in a request that does not contain a Range header field."
        if (!request.Headers.Contains("Range"))
        {
            throw new InvalidOperationException(
                "RFC 9110 §13.1.5: If-Range header MUST NOT be sent without a Range header.");
        }

        var ifRangeValue = GetSingleValue(ifRangeValues);
        if (string.IsNullOrWhiteSpace(ifRangeValue))
        {
            return;
        }

        if (IsEntityTag(ifRangeValue))
        {
            // Weak ETags are not allowed — RFC 9110 §13.1.5:
            // "A client MUST NOT generate an If-Range header field containing an entity-tag that is marked as weak."
            if (ifRangeValue.StartsWith("W/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RFC 9110 §13.1.5: If-Range MUST NOT contain a weak entity-tag.");
            }
        }
        else if (IsHttpDate(ifRangeValue))
        {
            // HTTP-date when an ETag is available is discouraged — RFC 9110 §13.1.5:
            // "A client SHOULD NOT generate an If-Range header field with an HTTP-date validator
            //  if the representation's entity-tag is available."
            // We treat this as a MUST for strict compliance when ETag header is present.
            if (request.Headers.TryGetValues("ETag", out _))
            {
                throw new InvalidOperationException(
                    "RFC 9110 §13.1.5: If-Range MUST use a strong entity-tag when an ETag is available, not an HTTP-date.");
            }
        }
    }

    private static string? GetSingleValue(IEnumerable<string> values)
    {
        using var enumerator = values.GetEnumerator();
        return enumerator.MoveNext() ? enumerator.Current : null;
    }

    private static bool IsEntityTag(string value)
    {
        // Entity-tags start with W/" (weak) or " (strong)
        return value.StartsWith('"') || value.StartsWith("W/\"", StringComparison.Ordinal);
    }

    private static bool IsHttpDate(string value)
    {
        return DateTimeOffset.TryParseExact(
            value,
            HttpDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out _);
    }
}
