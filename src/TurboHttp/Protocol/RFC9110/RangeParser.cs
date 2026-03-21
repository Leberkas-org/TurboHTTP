using System;

namespace TurboHttp.Protocol.RFC9110;

/// <summary>
/// RFC 9110 §14.1.1 — parses Content-Range header values.
/// <list type="bullet">
///   <item>Recipients MUST anticipate potentially large decimal numerals.</item>
///   <item>All byte positions use <see cref="long"/> to support ranges beyond 4 GB.</item>
/// </list>
/// </summary>
internal static class RangeParser
{
    /// <summary>
    /// Parsed Content-Range header value.
    /// </summary>
    /// <param name="Unit">The range unit (e.g., "bytes").</param>
    /// <param name="First">First byte position, or null for suffix/unsatisfied ranges.</param>
    /// <param name="Last">Last byte position, or null for suffix/unsatisfied ranges.</param>
    /// <param name="Length">Complete length, or null if unknown ("*").</param>
    internal sealed record ContentRangeValue(string Unit, long? First, long? Last, long? Length);

    /// <summary>
    /// Parses a Content-Range header value.
    /// Returns null if the format is unrecognised.
    /// </summary>
    /// <remarks>
    /// Supported formats:
    /// <list type="bullet">
    ///   <item><c>bytes 0-499/1234</c> — standard byte range</item>
    ///   <item><c>bytes -500/1234</c> — suffix range (last 500 bytes)</item>
    ///   <item><c>bytes 0-499/*</c> — unknown complete length</item>
    ///   <item><c>bytes */1234</c> — unsatisfied range</item>
    /// </list>
    /// </remarks>
    public static ContentRangeValue? Parse(string? contentRange)
    {
        if (string.IsNullOrWhiteSpace(contentRange))
        {
            return null;
        }

        var span = contentRange.AsSpan().Trim();

        // Find unit separator (first space)
        var spaceIndex = span.IndexOf(' ');
        if (spaceIndex <= 0)
        {
            return null;
        }

        var unit = span[..spaceIndex].ToString();
        var rest = span[(spaceIndex + 1)..].Trim();

        if (rest.IsEmpty)
        {
            return null;
        }

        // Find the "/" separator for range/length
        var slashIndex = rest.IndexOf('/');
        if (slashIndex < 0)
        {
            return null;
        }

        var rangePart = rest[..slashIndex].Trim();
        var lengthPart = rest[(slashIndex + 1)..].Trim();

        // Parse complete length
        long? length = null;
        if (!lengthPart.SequenceEqual("*"))
        {
            if (!long.TryParse(lengthPart, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedLength) || parsedLength < 0)
            {
                return null;
            }

            length = parsedLength;
        }

        // Unsatisfied range: "*/length"
        if (rangePart.SequenceEqual("*"))
        {
            return new ContentRangeValue(unit, null, null, length);
        }

        // Suffix range: "-NNN"
        if (rangePart.Length > 1 && rangePart[0] == '-' && rangePart[1..].IndexOf('-') < 0)
        {
            if (!long.TryParse(rangePart[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var suffixLength) || suffixLength < 0)
            {
                return null;
            }

            return new ContentRangeValue(unit, null, suffixLength, length);
        }

        // Standard range: "first-last"
        var dashIndex = rangePart.IndexOf('-');
        if (dashIndex <= 0)
        {
            return null;
        }

        var firstPart = rangePart[..dashIndex].Trim();
        var lastPart = rangePart[(dashIndex + 1)..].Trim();

        if (!long.TryParse(firstPart, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var first) || first < 0)
        {
            return null;
        }

        if (!long.TryParse(lastPart, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var last) || last < 0)
        {
            return null;
        }

        return new ContentRangeValue(unit, first, last, length);
    }
}
