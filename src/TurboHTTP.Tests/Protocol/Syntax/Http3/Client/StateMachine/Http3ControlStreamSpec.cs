using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Tests.Shared;
using Servus.Akka.Transport;
using System.Net;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3ControlStreamSpec
{
    private readonly FakeOps _ops = new();

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        TransportProtocol.Tcp);

    private Http3ClientStateMachine CreateMachine(FakeOps? ops = null)
        => new(new TurboClientOptions(), ops ?? _ops);

    private static TransportBuffer SerializeFrame(Http3Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void StateMachine_should_accept_settings_on_control_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();
        var settings = new SettingsFrame([(SettingsIdentifier.MaxFieldSectionSize, 16384)]);
        sm.DecodeServerData(new MultiplexedData(SerializeFrame(settings), -2));
        // No exception — SETTINGS accepted on control stream
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void SettingsIdentifier_should_reject_reserved_http2_identifiers()
    {
        var parameters = new List<(long Identifier, long Value)>
        {
            (SettingsIdentifier.ReservedH2EnablePush, 1),
        };

        var ex = Assert.Throws<HttpProtocolException>(
            () => SettingsIdentifier.RejectForbiddenH2Settings(parameters));
        Assert.Contains("reserved", ex.Message.ToLowerInvariant());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void SettingsFrame_should_accept_qpack_max_table_capacity()
    {
        var settings = new SettingsFrame([(SettingsIdentifier.QpackMaxTableCapacity, 4096)]);
        Assert.Single(settings.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void SettingsFrame_should_accept_qpack_blocked_streams()
    {
        var settings = new SettingsFrame([(SettingsIdentifier.QpackBlockedStreams, 100)]);
        Assert.Single(settings.Parameters);
    }
}
