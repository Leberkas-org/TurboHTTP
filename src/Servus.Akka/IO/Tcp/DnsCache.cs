using System.Collections.Concurrent;
using System.Net;

namespace Servus.Akka.IO.Tcp;

internal static class DnsCache
{
    private static readonly ConcurrentDictionary<string, DnsEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static TimeSpan _ttl = TimeSpan.FromSeconds(120);

    public static TimeSpan Ttl
    {
        get => _ttl;
        set => _ttl = value;
    }

    public static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        if (Cache.TryGetValue(host, out var entry) && !entry.IsExpired(_ttl))
        {
            return entry.Addresses;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        if (addresses.Length > 0)
        {
            Cache[host] = new DnsEntry(addresses, Environment.TickCount64);
        }

        return addresses;
    }

    internal static void Clear() => Cache.Clear();

    private readonly record struct DnsEntry(IPAddress[] Addresses, long TimestampMs)
    {
        public bool IsExpired(TimeSpan ttl)
            => Environment.TickCount64 - TimestampMs > (long)ttl.TotalMilliseconds;
    }
}
