using System;
using System.Collections.Generic;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Client;
using TurboHttp.IO;

namespace TurboHttp.Streams;

public sealed class HostRoutingStage : GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpRequestMessage> _inlet = new("host.pool.in");
    private readonly Outlet<HttpResponseMessage> _outlet = new("host.pool.out");
    private readonly TurboClientOptions _clientOptions;

    public override FlowShape<HttpRequestMessage, HttpResponseMessage> Shape { get; }

    internal Func<TcpOptions, Akka.Actor.ActorSystem, Action<HttpResponseMessage>, IHostConnectionPool>? PoolFactory;

    public HostRoutingStage(TurboClientOptions clientOptions)
    {
        _clientOptions = clientOptions;
        Shape = new FlowShape<HttpRequestMessage, HttpResponseMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly HostRoutingStage _stage;
        private readonly Dictionary<string, IHostConnectionPool> _pools = new();
        private readonly Queue<HttpResponseMessage> _responseBuffer = new();
        private bool _downstreamWaiting;

        private Action<HttpResponseMessage>? _onResponse;

        public Logic(HostRoutingStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var request = Grab(stage._inlet);
                    var uri = request.RequestUri!;
                    var options = TcpOptionsFactory.Build(uri, _stage._clientOptions);

                    var pool = GetOrCreatePool(uri.Scheme, options);

                    pool.Send(request);
                    Pull(stage._inlet);
                });

            SetHandler(stage._outlet,
                onPull: () =>
                {
                    if (_responseBuffer.TryDequeue(out var response))
                    {
                        Push(stage._outlet, response);
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                });
        }

        public override void PreStart()
        {
            _onResponse = GetAsyncCallback<HttpResponseMessage>(response =>
            {
                if (_downstreamWaiting)
                {
                    _downstreamWaiting = false;
                    Push(_stage._outlet, response);
                }
                else
                {
                    _responseBuffer.Enqueue(response);
                }
            });

            Pull(_stage._inlet);
        }

        private IHostConnectionPool GetOrCreatePool(string scheme, TcpOptions options)
        {
            var key = $"{scheme}:{options.Host}:{options.Port}";

            if (_pools.TryGetValue(key, out var pool))
            {
                return pool;
            }

            var system = (Materializer as ActorMaterializer)!.System;

            var factory = _stage.PoolFactory ?? ((opts, sys, cb) => new HostConnectionPool(opts, sys, cb));
            var newPool = factory(options, system, _onResponse!);

            _pools[key] = newPool;
            return newPool;
        }
    }
}