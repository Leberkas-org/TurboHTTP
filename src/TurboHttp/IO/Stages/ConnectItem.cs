namespace TurboHttp.IO.Stages;

public record ConnectItem(TcpOptions Options) : ISignalItem
{
    public HostKey Key { get; } = new HostKey
    {
        Host = Options.Host,
        Port = (ushort)Options.Port,
        Schema = Options is TlsOptions ? "https" : "http",
        HttpVersion = System.Net.HttpVersion.Unknown
    };
}