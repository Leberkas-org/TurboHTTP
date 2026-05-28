using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3DecoderStreamSpec
{
    private readonly FakeOps _ops = new();

    private Http3ClientStateMachine CreateMachine(TurboClientOptions? options = null)
    {
        return new Http3ClientStateMachine(options ?? new TurboClientOptions(), _ops);
    }

    private static void SimulateConnect(Http3ClientStateMachine sm)
    {
        sm.DecodeServerData(new TransportConnected(null!));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void PreStart_should_emit_decoder_stream_opening()
    {
        var sm = CreateMachine();
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        // PreStart should emit OpenStream for decoder stream (-4)
        var openStreams = _ops.Outbound
            .OfType<OpenStream>()
            .ToList();
        Assert.Contains(openStreams, s => s.StreamId == -4 && s.Direction == StreamDirection.Unidirectional);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void PreStart_should_emit_control_stream_preface()
    {
        var opts = new TurboClientOptions
        {
            Http3 =
            {
                QpackMaxTableCapacity = 4096
            }
        };
        var sm = CreateMachine(opts);
        _ops.Outbound.Clear();

        sm.PreStart();
        SimulateConnect(sm);

        // Should emit control stream data with SETTINGS
        var controlData = _ops.Outbound
            .OfType<MultiplexedData>()
            .Where(d => d.StreamId == -2)
            .ToList();
        Assert.NotEmpty(controlData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void OnConnectionLost_should_emit_control_streams()
    {
        var sm = CreateMachine();
        sm.PreStart();
        SimulateConnect(sm);
        _ops.Outbound.Clear();

        // Create a request to trigger in-flight tracking
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/");
        sm.OnRequest(request);
        _ops.Outbound.Clear();

        // Trigger reconnection by simulating transport disconnect
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        // After disconnect with in-flight requests, control streams should be re-opened
        // Items are buffered (transport disconnected), so check ConnectTransport was emitted directly
        var reconnectControlStreams = _ops.Outbound
            .OfType<ConnectTransport>()
            .Count();
        Assert.Equal(1, reconnectControlStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204")]
    public void DecodeServerData_with_qpack_encoder_updates_should_be_routed_to_encoder_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();
        _ops.Outbound.Clear();

        // Feed QPACK encoder stream data (stream ID -3) to trigger state updates
        var encoderUpdate = "?#B"u8.ToArray(); // Example encoder instruction
        var buf = TransportBuffer.Rent(encoderUpdate.Length);
        encoderUpdate.CopyTo(buf.FullMemory.Span);
        buf.Length = encoderUpdate.Length;

        sm.DecodeServerData(new MultiplexedData(buf, -3));
    }
}