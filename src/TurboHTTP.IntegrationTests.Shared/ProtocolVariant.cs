using Xunit.Sdk;

namespace TurboHTTP.IntegrationTests.Shared;

public sealed class ProtocolVariant : IXunitSerializable
{
    public TestHttpVersion Version { get; private set; }
    public bool Tls { get; private set; }

    [Obsolete("For deserialization only")]
    public ProtocolVariant()
    {
    }

    public ProtocolVariant(TestHttpVersion Version, bool Tls)
    {
        this.Version = Version;
        this.Tls = Tls;
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Version), Version);
        info.AddValue(nameof(Tls), Tls);
    }

    public void Deserialize(IXunitSerializationInfo info)
    {
        Version = info.GetValue<TestHttpVersion>(nameof(Version));
        Tls = info.GetValue<bool>(nameof(Tls));
    }

    public override string ToString() => Tls ? $"{Version}/TLS" : Version.ToString();
}