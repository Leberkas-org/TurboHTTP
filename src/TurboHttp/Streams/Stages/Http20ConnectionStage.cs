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

        private readonly int _initialRecvWindowSize;

        public Http20ConnectionStage(int initialRecvWindowSize = 65535)
        {
            _initialRecvWindowSize = initialRecvWindowSize;
        }

        public override BidiShape<Http2Frame, Http2Frame, Http2Frame, Http2Frame> Shape
            => new(_inletRaw, _outletStream, _inletRequest, _outletRaw);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly Http20ConnectionStage _stage;
            private int _connectionWindow;
            private int _initialRecvStreamWindow;
            private int _initialSendStreamWindow = 65535;
            private bool _goAwayReceived;

            private readonly Dictionary<int, int> _streamWindows = new();

            public Logic(Http20ConnectionStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _connectionWindow = stage._initialRecvWindowSize;
                _initialRecvStreamWindow = stage._initialRecvWindowSize;

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

                    if (_goAwayReceived)
                    {
                        FailStage(new Http2Exception("Connection received GOAWAY — new requests are not allowed"));
                        return;
                    }

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
                        // Server's InitialWindowSize controls how much the CLIENT can SEND per stream
                        _initialSendStreamWindow = (int)value;
                    }
                }

                Emit(_stage._outletRaw, new SettingsFrame([], isAck: true));
            }

            private void HandleInboundData(DataFrame frame)
            {
                var dataLength = frame.Data.Length;

                _connectionWindow -= dataLength;

                _streamWindows.TryAdd(frame.StreamId, _initialRecvStreamWindow);

                _streamWindows[frame.StreamId] -= dataLength;

                if (_connectionWindow < 0)
                {
                    FailStage(new Exception("Connection window exceeded"));
                }

                if (_streamWindows[frame.StreamId] < 0)
                {
                    FailStage(new Exception("Stream window exceeded"));
                }

                // RFC 9113 §6.9: WINDOW_UPDATE increment of 0 is a protocol error.
                // Skip window updates for empty DATA frames (e.g. END_STREAM-only frames).
                if (dataLength > 0)
                {
                    Emit(_stage._outletRaw, new WindowUpdateFrame(0, dataLength));
                    Emit(_stage._outletRaw, new WindowUpdateFrame(frame.StreamId, dataLength));
                }
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
                    _streamWindows.TryAdd(frame.StreamId, _initialRecvStreamWindow);

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
