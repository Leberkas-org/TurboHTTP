using System.IO.Pipelines;

namespace TurboHTTP.Protocol;

internal sealed class BodyHandle(long maxBodySize) : IDisposable
{
    private static readonly PipeOptions NoPausePipeOptions = new(pauseWriterThreshold: 0);

    private readonly Pipe _pipe = new(NoPausePipeOptions);
    private long _totalBytes;
    private bool _completed;

    public void Feed(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        _totalBytes += data.Length;
        if (_totalBytes > maxBodySize)
        {
            throw new HttpProtocolException($"Request body size {_totalBytes} exceeds limit {maxBodySize}.");
        }

        var memory = _pipe.Writer.GetSpan(data.Length);
        data.CopyTo(memory);
        _pipe.Writer.Advance(data.Length);
        _ = _pipe.Writer.FlushAsync();
    }

    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _pipe.Writer.Complete();
    }

    public void Abort(Exception reason)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _pipe.Writer.Complete(reason);
    }

    public Stream AsStream() => _pipe.Reader.AsStream();

    public void Dispose()
    {
        if (!_completed)
        {
            _completed = true;
            _pipe.Writer.Complete(new ObjectDisposedException(nameof(BodyHandle)));
        }

        _pipe.Reader.Complete();
    }
}