using System;

namespace TurboHttp.IO.Stages;

public record ConnectItem(TcpOptions Options, Version HttpVersion) : ISignalItem
{
    public HostKey Key { get; } = new HostKey
    {
        Host = Options.Host,
        Port = (ushort)Options.Port,
        Schema = Options is TlsOptions ? "https" : "http",
        HttpVersion = HttpVersion
    };
}