using System.Net;

namespace TurboHTTP.Internal;

internal readonly record struct RequestEndpoint
{
    public static RequestEndpoint FromRequest(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Version);
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        return new RequestEndpoint
        {
            Host = request.RequestUri.Host,
            Port = (ushort)request.RequestUri.Port,
            Scheme = request.RequestUri.Scheme,
            Version = request.Version
        };
    }

    public static RequestEndpoint Default => new()
    {
        Host = string.Empty,
        Port = ushort.MinValue,
        Scheme = string.Empty,
        Version = HttpVersion.Unknown
    };

    public required string Scheme { get; init; }
    public required string Host { get; init; }
    public required ushort Port { get; init; }
    public required Version Version { get; init; }

    public bool Equals(RequestEndpoint other) =>
        string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Scheme, other.Scheme, StringComparison.OrdinalIgnoreCase) &&
        Port == other.Port &&
        Version == other.Version;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Host, StringComparer.OrdinalIgnoreCase);
        hash.Add(Scheme, StringComparer.OrdinalIgnoreCase);
        hash.Add(Port);
        hash.Add(Version);
        return hash.ToHashCode();
    }
}