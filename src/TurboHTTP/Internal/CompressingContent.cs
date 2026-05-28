using System.Buffers;
using System.Net;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Internal;

internal sealed class CompressingContent : HttpContent
{
    private IMemoryOwner<byte>? _owner;
    private readonly int _length;

    public CompressingContent(HttpContent inner, string encoding)
    {
        foreach (var header in inner.Headers)
        {
            if (header.Key.Equals(WellKnownHeaders.ContentEncoding, StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals(WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        Headers.TryAddWithoutValidation(WellKnownHeaders.ContentEncoding, encoding);

        using var output = RecyclableStreams.Manager.GetStream();
        using (var compressor = ContentEncoding.CreateCompressor(output, encoding))
        {
            using var source = inner.ReadAsStream();
            source.CopyTo(compressor);
        }

        _length = (int)output.Length;
        _owner = MemoryPool<byte>.Shared.Rent(_length);
        output.Position = 0;
        output.ReadExactly(_owner.Memory.Span[.._length]);
        Headers.ContentLength = _length;
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken ct)
    {
        var owner = AcquireOwner();
        stream.Write(owner.Memory.Span[.._length]);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var owner = AcquireOwner();
        var vt = stream.WriteAsync(owner.Memory[.._length]);
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
    {
        var owner = AcquireOwner();
        var vt = stream.WriteAsync(owner.Memory[.._length], ct);
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Dispose();
        }

        base.Dispose(disposing);
    }

    private IMemoryOwner<byte> AcquireOwner()
    {
        var owner = Interlocked.CompareExchange(ref _owner, null, null);
        ObjectDisposedException.ThrowIf(owner is null, this);
        return owner;
    }
}
