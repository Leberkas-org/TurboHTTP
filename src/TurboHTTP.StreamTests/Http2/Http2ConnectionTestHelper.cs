using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.StreamTests.Http2;

/// <summary>
/// Test helper for the combined Http20ConnectionStage.
/// Serializes Http2Frame objects into NetworkBuffer (IInputItem) for InServer,
/// and decodes NetworkBuffer (IOutputItem) back into Http2Frame for assertions.
/// </summary>
internal static class Http2ConnectionTestHelper
{
    /// <summary>
    /// Serialize one or more Http2Frame objects into a single NetworkBuffer.
    /// </summary>
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

    /// <summary>
    /// Convert a sequence of Http2Frame into IInputItem sequence (one NetworkBuffer per frame).
    /// </summary>
    public static IEnumerable<IInputItem> FramesToInputs(IEnumerable<Http2Frame> frames)
    {
        foreach (var f in frames)
        {
            yield return FramesToInput(f);
        }
    }

    /// <summary>
    /// Decode all Http2Frame objects from IOutputItem results.
    /// Filters out IControlItem signals and only processes NetworkBuffer items.
    /// When <paramref name="skipPreface"/> is true, the first NetworkBuffer (connection preface)
    /// is skipped — use this when testing protocol responses from the combined stage.
    /// </summary>
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

    /// <summary>
    /// Extract only IControlItem signals from IOutputItem results.
    /// </summary>
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
