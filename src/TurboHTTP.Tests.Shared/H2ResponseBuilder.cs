using System.Text;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fluent builder for constructing HTTP/2 frame-level byte arrays.
/// Produces valid frame sequences decodable by <see cref="FrameDecoder"/>.
/// Intended for byte-level acceptance tests where hand-crafting frames is verbose.
/// </summary>
public sealed class H2ResponseBuilder
{
    private readonly List<Http2Frame> _frames = [];
    private readonly HpackEncoder _encoder;

    public H2ResponseBuilder(bool useHuffman = false)
    {
        _encoder = new HpackEncoder(useHuffman: useHuffman);
    }

    /// <summary>
    /// Appends a SETTINGS frame with the given parameters on stream 0.
    /// </summary>
    public H2ResponseBuilder Settings(params (SettingsParameter Key, uint Value)[] parameters)
    {
        _frames.Add(new SettingsFrame(parameters));
        return this;
    }

    /// <summary>
    /// Appends a SETTINGS ACK frame on stream 0.
    /// </summary>
    public H2ResponseBuilder SettingsAck()
    {
        _frames.Add(new SettingsFrame([], isAck: true));
        return this;
    }

    /// <summary>
    /// Appends a HEADERS frame with HPACK-encoded pseudo-headers and regular headers.
    /// </summary>
    public H2ResponseBuilder Headers(int streamId, int status, IReadOnlyList<(string Name, string Value)>? headers = null, bool endStream = false)
    {
        var allHeaders = new List<(string, string)>
        {
            (":status", status.ToString())
        };

        if (headers != null)
        {
            allHeaders.AddRange(headers);
        }

        var encoded = _encoder.Encode(allHeaders);
        _frames.Add(new HeadersFrame(streamId, encoded, endStream: endStream, endHeaders: true));
        return this;
    }

    /// <summary>
    /// Appends a DATA frame with the given body bytes.
    /// </summary>
    public H2ResponseBuilder Data(int streamId, ReadOnlyMemory<byte> body, bool endStream = true)
    {
        _frames.Add(new DataFrame(streamId, body, endStream: endStream));
        return this;
    }

    /// <summary>
    /// Appends a DATA frame with a UTF-8 string body.
    /// </summary>
    public H2ResponseBuilder Data(int streamId, string body, bool endStream = true)
    {
        return Data(streamId, Encoding.UTF8.GetBytes(body), endStream);
    }

    /// <summary>
    /// Appends a WINDOW_UPDATE frame.
    /// </summary>
    public H2ResponseBuilder WindowUpdate(int streamId, int increment)
    {
        _frames.Add(new WindowUpdateFrame(streamId, increment));
        return this;
    }

    /// <summary>
    /// Appends a PING frame (8 bytes of opaque data).
    /// </summary>
    public H2ResponseBuilder Ping(ReadOnlyMemory<byte>? data = null, bool isAck = false)
    {
        var pingData = data ?? new byte[8];
        _frames.Add(new PingFrame(pingData, isAck: isAck));
        return this;
    }

    /// <summary>
    /// Appends a GOAWAY frame on stream 0.
    /// </summary>
    public H2ResponseBuilder GoAway(int lastStreamId, Http2ErrorCode errorCode = Http2ErrorCode.NoError)
    {
        _frames.Add(new GoAwayFrame(lastStreamId, errorCode));
        return this;
    }

    /// <summary>
    /// Appends a RST_STREAM frame.
    /// </summary>
    public H2ResponseBuilder RstStream(int streamId, Http2ErrorCode errorCode)
    {
        _frames.Add(new RstStreamFrame(streamId, errorCode));
        return this;
    }

    /// <summary>
    /// Serializes all appended frames into a single contiguous byte array.
    /// </summary>
    public byte[] Build()
    {
        var totalSize = 0;
        foreach (var frame in _frames)
        {
            totalSize += frame.SerializedSize;
        }

        var result = new byte[totalSize];
        var span = result.AsSpan();

        foreach (var frame in _frames)
        {
            frame.WriteTo(ref span);
        }

        return result;
    }
}
