using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.StreamTests.Http2;

internal static class Http2ConnectionTestHelper
{
    public static IInputItem FramesToInput(params Http2Frame[] frames)
    {
        var totalSize = 0;
        foreach (var f in frames)
        {
            totalSize += f.SerializedSize;
        }

        var buf = NetworkBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        foreach (var f in frames)
        {
            f.WriteTo(ref span);
        }

        buf.Length = totalSize;
        return buf;
    }

    public static IEnumerable<IInputItem> FramesToInputs(IEnumerable<Http2Frame> frames)
    {
        foreach (var f in frames)
        {
            yield return FramesToInput(f);
        }
    }

    public static IReadOnlyList<Http2Frame> DecodeFrames(IEnumerable<IOutputItem> items, bool skipPreface = false)
    {
        var decoder = new FrameDecoder();
        var result = new List<Http2Frame>();
        var skippedFirst = false;
        foreach (var item in items)
        {
            if (item is NetworkBuffer buffer)
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

    public static IReadOnlyList<IControlItem> ExtractSignals(IEnumerable<IOutputItem> items)
    {
        var result = new List<IControlItem>();
        foreach (var item in items)
        {
            if (item is IControlItem signal)
            {
                result.Add(signal);
            }
        }

        return result;
    }
}