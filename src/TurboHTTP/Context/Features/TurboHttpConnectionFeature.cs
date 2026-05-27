using System.Net;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboHttpConnectionFeature : IHttpConnectionFeature
{
    public string ConnectionId { get; set; } = string.Empty;

    public IPAddress? RemoteIpAddress { get; set; }

    public int RemotePort { get; set; }

    public IPAddress? LocalIpAddress { get; set; }

    public int LocalPort { get; set; }
}