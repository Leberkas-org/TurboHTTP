using System;
using System.Net.Http;

namespace TurboHttp.IO.Stages;

public record struct HostKey
{
    public static HostKey FromRequest(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Version);
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        return new HostKey
        {
            Host = request.RequestUri.Host,
            Port = (ushort)request.RequestUri.Port,
            Schema = request.RequestUri.Scheme,
            HttpVersion = request.Version
        };
    }

    public static HostKey Default => new()
    {
        Host = string.Empty, Port = ushort.MinValue, Schema = string.Empty, HttpVersion = System.Net.HttpVersion.Unknown
    };

    public required string Schema { get; init; }
    public required string Host { get; init; }
    public required ushort Port { get; init; }
    public required Version HttpVersion { get; init; }

    public string Key => $"{Host}:{Port}:{Schema}:{HttpVersion}";
}