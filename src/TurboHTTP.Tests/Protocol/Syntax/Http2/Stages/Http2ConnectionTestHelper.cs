using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Stages;

internal static class Http2ConnectionTestHelper
{
    public static ITransportInbound FramesToInput(params Http2Frame[] frames)
    {
        var totalSize = 0;
        foreach (var f in frames)
        {
            totalSize += f.SerializedSize;
        }

        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        foreach (var f in frames)
        {
            f.WriteTo(ref span);
        }

        buf.Length = totalSize;
        return new TransportData(buf);
    }

    public static IEnumerable<ITransportInbound> FramesToInputs(IEnumerable<Http2Frame> frames)
    {
        foreach (var f in frames)
        {
            yield return FramesToInput(f);
        }
    }

    public static IReadOnlyList<Http2Frame> DecodeFrames(IEnumerable<ITransportOutbound> items,
        bool skipPreface = false)
    {
        var decoder = new FrameDecoder();
        var result = new List<Http2Frame>();
        var skippedFirst = false;
        foreach (var item in items)
        {
            if (item is TransportData { Buffer: var buffer })
            {
                if (skipPreface && !skippedFirst)
                {
                    skippedFirst = true;
                    continue;
                }

                var frames = decoder.Decode(buffer);
                result.AddRange(frames);
            }
        }

        return result;
    }

    public static IReadOnlyList<ITransportOutbound> ExtractSignals(IEnumerable<ITransportOutbound> items)
    {
        var result = new List<ITransportOutbound>();
        foreach (var item in items)
        {
            // Exclude data items, include control messages (Connect, Disconnect, OpenStream, CloseStream, etc.)
            if (item is not TransportData)
            {
                result.Add(item);
            }
        }

        return result;
    }
}