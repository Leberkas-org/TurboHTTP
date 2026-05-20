using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;

namespace TurboHTTP.Server;

public sealed class TurboConnectionInfo : ConnectionInfo
{
    private SslStream? _sslStream;
    private bool _allowDelayedNegotiation;
    private SslApplicationProtocol _negotiatedProtocol;
    private Servus.Akka.Transport.SecurityInfo? _securityInfo;

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

    internal void SetTlsState(SslStream? sslStream, bool allowDelayedNegotiation)
    {
        _sslStream = sslStream;
        _allowDelayedNegotiation = allowDelayedNegotiation;
    }

    internal void SetNegotiatedProtocol(SslApplicationProtocol protocol)
    {
        _negotiatedProtocol = protocol;
    }

    internal Servus.Akka.Transport.SecurityInfo? SecurityInfo => _securityInfo;

    internal void SetSecurityInfo(Servus.Akka.Transport.SecurityInfo securityInfo)
    {
        _securityInfo = securityInfo;
    }

    internal void SetClientCertificateFromHandshake(SslStream sslStream)
    {
        if (sslStream.RemoteCertificate is X509Certificate2 cert)
        {
            ClientCertificate = cert;
        }
    }

    public override async Task<X509Certificate2?> GetClientCertificateAsync(
        CancellationToken cancellationToken = default)
    {
        if (ClientCertificate is not null)
        {
            return ClientCertificate;
        }

        if (_sslStream is null || !_allowDelayedNegotiation)
        {
            return null;
        }

        if (_negotiatedProtocol != SslApplicationProtocol.Http11 &&
            _negotiatedProtocol != default)
        {
            throw new InvalidOperationException(
                "Delayed client certificate negotiation is only supported on HTTP/1.1 connections.");
        }

        await _sslStream.NegotiateClientCertificateAsync(cancellationToken);
        ClientCertificate = _sslStream.RemoteCertificate as X509Certificate2;
        return ClientCertificate;
    }
}