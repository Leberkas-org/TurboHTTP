using System.Net.Security;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol;

internal sealed class ProtocolNegotiatingStateMachine : IServerStateMachine
{
    private enum Phase { WaitingForConnect, Sniffing, Running }

    private readonly TurboServerOptions _options;
    private readonly UpgradeAwareOps _wrappedOps;

    private Phase _phase = Phase.WaitingForConnect;
    private IServerStateMachine? _inner;
    private readonly List<ITransportInbound> _buffered = [];

    public bool CanAcceptResponse => _phase == Phase.Running && _inner!.CanAcceptResponse;
    public bool ShouldComplete => _phase == Phase.Running && _inner!.ShouldComplete;

    public ProtocolNegotiatingStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _options = options;
        _wrappedOps = new UpgradeAwareOps(ops, this);
    }

    public void PreStart()
    {
        if (_phase == Phase.Running)
        {
            _inner!.PreStart();
        }
    }

    public void DecodeClientData(ITransportInbound data)
    {
        switch (_phase)
        {
            case Phase.WaitingForConnect:
                OnWaitingForConnect(data);
                break;
            case Phase.Sniffing:
                OnSniffing(data);
                break;
            case Phase.Running:
                _inner!.DecodeClientData(data);
                break;
        }
    }

    public void OnResponse(TurboHttpContext context) => _inner!.OnResponse(context);
    public void OnDownstreamFinished() => _inner?.OnDownstreamFinished();
    public void OnTimerFired(string name) => _inner?.OnTimerFired(name);
    public void OnBodyMessage(object msg) => _inner?.OnBodyMessage(msg);

    public void Cleanup()
    {
        _inner?.Cleanup();
        DisposeBuffered();
    }

    private void OnWaitingForConnect(ITransportInbound data)
    {
        if (data is not TransportConnected { Info.Security: var security })
        {
            return;
        }

        if (security?.ApplicationProtocol == SslApplicationProtocol.Http2)
        {
            Activate(ops => new Http2ServerStateMachine(_options, ops));
            _inner!.DecodeClientData(data);
            return;
        }

        if (security is not null)
        {
            Activate(ops => new Http11ServerStateMachine(_options, ops));
            _inner!.DecodeClientData(data);
            return;
        }

        _buffered.Add(data);
        _phase = Phase.Sniffing;
    }

    private void OnSniffing(ITransportInbound data)
    {
        _buffered.Add(data);

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        var span = buffer.Memory.Span;
        if (span.Length < 4)
        {
            return;
        }

        if (span[0] == 'P' && span[1] == 'R' && span[2] == 'I' && span[3] == ' ')
        {
            Activate(ops => new Http2ServerStateMachine(_options, ops));
        }
        else
        {
            Activate(ops => new Http11ServerStateMachine(_options, ops));
        }

        ReplayBuffered();
    }

    private void Activate(Func<IServerStageOperations, IServerStateMachine> factory)
    {
        _inner = factory(_wrappedOps);
        _phase = Phase.Running;
        _inner.PreStart();
    }

    private void ReplayBuffered()
    {
        var buffered = _buffered.ToArray();
        _buffered.Clear();

        foreach (var item in buffered)
        {
            _inner!.DecodeClientData(item);
        }
    }

    private void DisposeBuffered()
    {
        foreach (var item in _buffered)
        {
            if (item is TransportData { Buffer: var buf })
            {
                buf.Dispose();
            }
        }

        _buffered.Clear();
    }

    internal void HandleUpgrade(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
    {
        _inner?.Cleanup();
        _inner = newSmFactory(_wrappedOps);
        _inner.PreStart();
    }

    private sealed class UpgradeAwareOps : IServerStageOperations, IProtocolSwitchCapable
    {
        private readonly IServerStageOperations _real;
        private readonly ProtocolNegotiatingStateMachine _parent;

        public UpgradeAwareOps(IServerStageOperations real, ProtocolNegotiatingStateMachine parent)
        {
            _real = real;
            _parent = parent;
        }

        public void OnRequest(TurboHttpContext context) => _real.OnRequest(context);
        public void OnOutbound(ITransportOutbound item) => _real.OnOutbound(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => _real.OnScheduleTimer(name, delay);
        public void OnCancelTimer(string name) => _real.OnCancelTimer(name);
        public ILoggingAdapter Log => _real.Log;
        public IActorRef StageActor => _real.StageActor;

        public void RequestProtocolSwitch(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
        {
            _parent.HandleUpgrade(newSmFactory);
        }
    }
}
