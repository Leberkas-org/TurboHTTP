using System.Globalization;

namespace TurboHTTP.Protocol.Semantics;

internal static class DateHeaderCache
{
    private static string _cachedValue = FormatNow();
    private static long _cachedTicks = Environment.TickCount64;

    public static string GetValue()
    {
        var now = Environment.TickCount64;
        if (now - _cachedTicks >= 1000)
        {
            _cachedTicks = now;
            _cachedValue = FormatNow();
        }

        return _cachedValue;
    }

    private static string FormatNow()
    {
        return DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
    }
}
