using System.Net;

namespace TurboHTTP.Context.Features;

public interface ITurboConnectionFeature
{
    string ConnectionId { get; set; }
    IPAddress? LocalIpAddress { get; set; }
    int LocalPort { get; set; }
    IPAddress? RemoteIpAddress { get; set; }
    int RemotePort { get; set; }
}
