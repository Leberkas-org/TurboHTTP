using System;
using System.Collections.Generic;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

public sealed class Http20ConnectionStage : GraphStage<BidiShape<Http2Frame, Http2Frame, Http2Frame, Http2Frame>>
    {
        private readonly Inlet<Http2Frame> _inletRaw = new("h2.server.in");
        private readonly Outlet<Http2Frame> _outletStream = new("h2.app.out");
        private readonly Inlet<Http2Frame> _inletRequest = new("h2.app.in");
        private readonly Outlet<Http2Frame> _outletRaw = new("h2.server.out");

        public override BidiShape<Http2Frame, Http2Frame, Http2Frame, Http2Frame> Shape
            => new(_inletRaw, _outletStream, _inletRequest, _outletRaw);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly Http20ConnectionStage _stage;
            private int _connectionWindow = 65535;
            private int _initialStreamWindow = 65535;
            private bool _goAwayReceived;

            private readonly Dictionary<int, int> _streamWindows = new();

            public Logic(Http20ConnectionStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage._inletRaw, onPush: () =>
                {
                    var frame = Grab(stage._inletRaw);

                    switch (frame)
                    {
                        case SettingsFrame settings:
                            HandleSettings(settings);
                            break;

                        case DataFrame data:
                            HandleInboundData(data);
                            break;

                        case WindowUpdateFrame win:
                            HandleWindowUpdate(win);
                            break;

                        case PingFrame ping:
                            HandlePing(ping);
                            return;

                        case GoAwayFrame:
                            _goAwayReceived = true;
                            break;
                    }

                    Push(stage._outletStream, frame);
                });

                SetHandler(stage._outletStream, onPull: () => Pull(stage._inletRaw));

                SetHandler(stage._inletRequest, onPush: () =>
                {
                    var frame = Grab(stage._inletRequest);

                    switch (frame)
                    {
                        case DataFrame data:
                            HandleOutboundData(data);
                            break;
                    }

                    Push(stage._outletRaw, frame);
                }, onUpstreamFinish: () =>
                {
                    // Request stream finished — keep stage alive to receive server responses.
                });

                SetHandler(stage._outletRaw, onPull: () =>
                {
                    if (!HasBeenPulled(stage._inletRequest))
                    {
                        Pull(stage._inletRequest);
                    }
                });
            }

            private void HandleSettings(SettingsFrame frame)
            {
                if (frame.IsAck)
                {
                    return;
                }

                foreach (var (key, value) in frame.Parameters)
                {
                    if (key == SettingsParameter.InitialWindowSize)
                    {
                        _initialStreamWindow = (int)value;
                    }
                }

                Emit(_stage._outletRaw, new SettingsFrame([], isAck: true));
            }

            private void HandleInboundData(DataFrame frame)
            {
                _connectionWindow -= frame.Data.Length;

                _streamWindows.TryAdd(frame.StreamId, _initialStreamWindow);

                _streamWindows[frame.StreamId] -= frame.Data.Length;

                if (_connectionWindow < 0)
                {
                    FailStage(new Exception("Connection window exceeded"));
                }

                if (_streamWindows[frame.StreamId] < 0)
                {
                    FailStage(new Exception("Stream window exceeded"));
                }

                Emit(_stage._outletRaw, new WindowUpdateFrame(0, frame.Data.Length));

                Emit(_stage._outletRaw, new WindowUpdateFrame(frame.StreamId, frame.Data.Length));
            }

            private void HandlePing(PingFrame ping)
            {
                if (!ping.IsAck)
                {
                    Emit(_stage._outletRaw, new PingFrame(ping.Data, true));
                }
            }

            private void HandleWindowUpdate(WindowUpdateFrame frame)
            {
                if (frame.StreamId == 0)
                {
                    _connectionWindow += frame.Increment;
                }
                else
                {
                    _streamWindows.TryAdd(frame.StreamId, _initialStreamWindow);

                    _streamWindows[frame.StreamId] += frame.Increment;
                }
            }

            private void HandleOutboundData(DataFrame frame)
            {
                _connectionWindow -= frame.Data.Length;

                if (_connectionWindow < 0)
                {
                    FailStage(new Exception("Outbound flow control exceeded"));
                }
            }
        }
    }
