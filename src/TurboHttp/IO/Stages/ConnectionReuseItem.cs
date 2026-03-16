using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.IO.Stages;

public record ConnectionReuseItem(HostKey Key, ConnectionReuseDecision Decision) : IControlItem;