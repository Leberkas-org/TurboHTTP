using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11UpgradeH2cSpec
{
    private sealed class SwitchCapableOps : IServerStageOperations, IProtocolSwitchCapable
    {
        private readonly FakeServerOps _inner = new();
        public Func<IServerStageOperations, IServerStateMachine>? SwitchFactory { get; private set; }

        public List<IFeatureCollection> Requests => _inner.Requests;
        public List<ITransportOutbound> Outbound => _inner.Outbound;
        public List<(string Name, TimeSpan Delay)> ScheduledTimers => _inner.ScheduledTimers;
        public List<string> CancelledTimers => _inner.CancelledTimers;
        public ILoggingAdapter Log => _inner.Log;
        public IActorRef StageActor { get => _inner.StageActor; set => _inner.StageActor = value; }
        public IMaterializer Materializer { get => _inner.Materializer; set => _inner.Materializer = value; }

        public void OnRequest(IFeatureCollection features) => _inner.OnRequest(features);
        public void OnOutbound(ITransportOutbound item) => _inner.OnOutbound(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => _inner.OnScheduleTimer(name, delay);
        public void OnCancelTimer(string name) => _inner.OnCancelTimer(name);

        public void RequestProtocolSwitch(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
        {
            SwitchFactory = newSmFactory;
        }
    }

    private static TransportData MakeData(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return new TransportData(buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void DecodeClientData_should_trigger_switch_when_upgrade_h2c_with_switchable_ops()
    {
        var ops = new SwitchCapableOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeData(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: Upgrade, HTTP2-Settings\r\n" +
            "Upgrade: h2c\r\n" +
            "HTTP2-Settings: AAMAAABkAAQBAAAAAAIAAAAA\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"));

        Assert.NotNull(ops.SwitchFactory);
        var outbound = ops.Outbound.OfType<TransportData>().ToList();
        Assert.NotEmpty(outbound);
        var responseText = Encoding.ASCII.GetString(outbound[0].Buffer.Span);
        Assert.Contains("101", responseText);
        Assert.Contains("Upgrade: h2c", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void DecodeClientData_should_ignore_upgrade_when_ops_not_switchable()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeData(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: Upgrade, HTTP2-Settings\r\n" +
            "Upgrade: h2c\r\n" +
            "HTTP2-Settings: AAMAAABkAAQBAAAAAAIAAAAA\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"));

        Assert.Single(ops.Requests);
        Assert.Equal("GET", ops.Requests[0].Get<IHttpRequestFeature>().Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void DecodeClientData_should_ignore_upgrade_without_http2_settings()
    {
        var ops = new SwitchCapableOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeData(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: Upgrade\r\n" +
            "Upgrade: h2c\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"));

        Assert.Null(ops.SwitchFactory);
        Assert.Single(ops.Requests);
    }
}


