using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Server;

public sealed class TurboConnectionInfo : ConnectionInfo
{
    public override string Id { get; set; }
    public override IPAddress? RemoteIpAddress { get; set; }
    public override int RemotePort { get; set; }
    public override IPAddress? LocalIpAddress { get; set; }
    public override int LocalPort { get; set; }
    public override X509Certificate2? ClientCertificate { get; set; }

    public TurboConnectionInfo(
        string id,
        IPAddress? remoteIpAddress,
        int remotePort,
        IPAddress? localIpAddress,
        int localPort)
    {
        Id = id;
        RemoteIpAddress = remoteIpAddress;
        RemotePort = remotePort;
        LocalIpAddress = localIpAddress;
        LocalPort = localPort;
    }

    public override Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ClientCertificate);
}