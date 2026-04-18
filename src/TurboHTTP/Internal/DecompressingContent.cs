using System.Net;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Internal;

internal sealed class DecompressingContent : HttpContent
{
    private HttpContent? _inner;
    private readonly string _encoding;

    public DecompressingContent(HttpContent inner, string encoding)
    {
        _inner = inner;
        _encoding = encoding;
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken ct)
    {
        var inner = AcquireInner();
        using var source = inner.ReadAsStream(ct);
        try
        {
            using var decompressor = ContentEncoding.CreateDecompressor(source, _encoding);
            decompressor.CopyTo(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpDecoderException)
        {
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var inner = AcquireInner();
        await using var source = await inner.ReadAsStreamAsync().ConfigureAwait(false);
        try
        {
            await using var decompressor = ContentEncoding.CreateDecompressor(source, _encoding);
            await decompressor.CopyToAsync(stream).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpDecoderException)
        {
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
    {
        var inner = AcquireInner();
        await using var source = await inner.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try
        {
            await using var decompressor = ContentEncoding.CreateDecompressor(source, _encoding);
            await decompressor.CopyToAsync(stream, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpDecoderException)
        {
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var inner = Interlocked.Exchange(ref _inner, null);
            inner?.Dispose();
        }

        base.Dispose(disposing);
    }

    private HttpContent AcquireInner()
    {
        var inner = Interlocked.CompareExchange(ref _inner, null, null);
        ObjectDisposedException.ThrowIf(inner is null, this);
        return inner;
    }
}
