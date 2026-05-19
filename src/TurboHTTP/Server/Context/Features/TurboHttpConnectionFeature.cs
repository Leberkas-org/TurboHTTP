using System.Net;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Server.Context.Features;

internal sealed class TurboHttpConnectionFeature(TurboConnectionInfo info) : IHttpConnectionFeature
{
    private readonly TurboConnectionInfo _info = info ?? throw new ArgumentNullException(nameof(info));

    public string ConnectionId
    {
        get => _info.Id;
        set => _info.Id = value;
    }

    public IPAddress? RemoteIpAddress
    {
        get => _info.RemoteIpAddress;
        set => _info.RemoteIpAddress = value;
    }

    public int RemotePort
    {
        get => _info.RemotePort;
        set => _info.RemotePort = value;
    }

    public IPAddress? LocalIpAddress
    {
        get => _info.LocalIpAddress;
        set => _info.LocalIpAddress = value;
    }

    public int LocalPort
    {
        get => _info.LocalPort;
        set => _info.LocalPort = value;
    }
}