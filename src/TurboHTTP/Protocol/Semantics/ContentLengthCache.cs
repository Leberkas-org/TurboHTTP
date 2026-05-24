using System.Globalization;

namespace TurboHTTP.Protocol.Semantics;

internal static class ContentLengthCache
{
    private static readonly string[] SmallValues = BuildSmallValues();

    private static string[] BuildSmallValues()
    {
        var values = new string[2 * 1024 + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    public static string GetValue(long contentLength)
    {
        if (contentLength >= 0 && contentLength < SmallValues.Length)
        {
            return SmallValues[contentLength];
        }

        return contentLength.ToString(CultureInfo.InvariantCulture);
    }
}
