using Microsoft.IO;

namespace TurboHTTP.Internal;

/// <summary>
/// Shared <see cref="RecyclableMemoryStreamManager"/> singleton for reducing GC pressure
/// from temporary <see cref="System.IO.MemoryStream"/> allocations in hot paths.
/// All streams obtained via <see cref="Manager"/> must be disposed after use so their
/// backing buffers are returned to the pool.
/// </summary>
internal static class RecyclableStreams
{
    internal static readonly RecyclableMemoryStreamManager Manager = new();
}
