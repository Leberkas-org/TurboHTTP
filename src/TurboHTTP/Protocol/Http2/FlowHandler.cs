using Servus.Akka.Transport;

namespace TurboHTTP.Protocol.Http2;

internal sealed class FlowHandler
{
    private readonly ConnectionState _connection;

    public FlowHandler(int connectionWindowSize, int streamWindowSize)
    {
        _connection = new ConnectionState(connectionWindowSize, streamWindowSize);
    }

    public bool GoAwayReceived => _connection.GoAwayReceived;

    public FlowControlResult OnInboundData(int streamId, int length)
    {
        return _connection.OnInboundData(streamId, length);
    }

    public void OnWindowUpdate(WindowUpdateFrame frame, RequestEncoder requestEncoder)
    {
        _connection.OnWindowUpdate(frame);
        if (frame.StreamId == 0)
        {
            requestEncoder.UpdateConnectionWindow(frame.Increment);
        }
        else
        {
            requestEncoder.UpdateStreamWindow(frame.StreamId, frame.Increment);
        }
    }

    public WindowUpdateFrame? OnStreamClosed(int streamId)
    {
        return _connection.OnStreamClosed(streamId);
    }

    public SettingsResult OnRemoteSettings(SettingsFrame frame)
    {
        return _connection.OnRemoteSettings(frame);
    }

    public PingFrame? OnPing(PingFrame ping)
    {
        return _connection.OnPing(ping);
    }

    public void OnGoAway()
    {
        _connection.OnGoAway();
    }

    public void Reset(int connectionWindowSize, int streamWindowSize)
    {
        _connection.Reset(connectionWindowSize, streamWindowSize);
    }
}
