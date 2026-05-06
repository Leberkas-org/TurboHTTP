using System.Text;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Protocol.Http3.Qpack;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fluent builder for constructing HTTP/3 frame-level byte arrays.
/// Produces valid frame sequences decodable by <see cref="FrameDecoder"/>.
/// Intended for byte-level acceptance tests where hand-crafting frames is verbose.
/// </summary>
public sealed class H3ResponseBuilder
{
    private readonly List<Http3Frame> _frames = [];
    private readonly QpackEncoder _encoder;

    /// <summary>
    /// Creates a new builder. Set <paramref name="maxTableCapacity"/> to 0
    /// to disable dynamic table usage (simplest for most tests).
    /// </summary>
    public H3ResponseBuilder(int maxTableCapacity = 0)
    {
        _encoder = new QpackEncoder(maxTableCapacity: maxTableCapacity);
    }

    /// <summary>
    /// Appends a SETTINGS frame with the given parameters.
    /// </summary>
    public H3ResponseBuilder Settings(params (long Identifier, long Value)[] parameters)
    {
        _frames.Add(new SettingsFrame(parameters));
        return this;
    }

    /// <summary>
    /// Appends a HEADERS frame with QPACK-encoded pseudo-headers and regular headers.
    /// </summary>
    public H3ResponseBuilder Headers(int status, IReadOnlyList<(string Name, string Value)>? headers = null, bool endStream = false)
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
        var frame = new HeadersFrame(encoded);
        _frames.Add(frame);
        return this;
    }

    /// <summary>
    /// Appends a DATA frame with the given body bytes.
    /// </summary>
    public H3ResponseBuilder Data(ReadOnlyMemory<byte> body)
    {
        _frames.Add(new DataFrame(body));
        return this;
    }

    /// <summary>
    /// Appends a DATA frame with a UTF-8 string body.
    /// </summary>
    public H3ResponseBuilder Data(string body)
    {
        return Data(Encoding.UTF8.GetBytes(body));
    }

    /// <summary>
    /// Appends a GOAWAY frame.
    /// </summary>
    public H3ResponseBuilder GoAway(long streamId)
    {
        _frames.Add(new GoAwayFrame(streamId));
        return this;
    }

    /// <summary>
    /// Appends a MAX_PUSH_ID frame.
    /// </summary>
    public H3ResponseBuilder MaxPushId(long pushId)
    {
        _frames.Add(new MaxPushIdFrame(pushId));
        return this;
    }

    /// <summary>
    /// Appends a CANCEL_PUSH frame.
    /// </summary>
    public H3ResponseBuilder CancelPush(long pushId)
    {
        _frames.Add(new CancelPushFrame(pushId));
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
