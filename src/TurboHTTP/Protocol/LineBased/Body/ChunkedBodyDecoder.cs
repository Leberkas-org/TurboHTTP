using System.Globalization;
using System.Text;

namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ChunkedBodyDecoder : IBodyDecoder
{
    private enum Phase
    {
        ChunkSize,
        ChunkData,
        ChunkDataCrlf,
        Trailer,
        Complete
    }

    private readonly BodyHandle _handle;
    private Phase _phase = Phase.ChunkSize;
    private int _currentChunkRemaining;
    private byte[] _stash = [];
    private int _stashLen;


    public bool IsBuffered => false;

    public ChunkedBodyDecoder(long maxBodySize = 10_485_760)
    {
        _handle = new BodyHandle(maxBodySize);
    }

    public bool Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        consumed = 0;
        if (_phase == Phase.Complete)
        {
            return true;
        }

        ReadOnlySpan<byte> work;
        var stashOffset = _stashLen;
        if (_stashLen > 0)
        {
            EnsureStash(_stashLen + data.Length);
            data.CopyTo(_stash.AsSpan(_stashLen));
            work = _stash.AsSpan(0, _stashLen + data.Length);
        }
        else
        {
            work = data;
        }

        var pos = 0;
        while (pos < work.Length)
        {
            switch (_phase)
            {
                case Phase.ChunkSize:
                    {
                        var crlf = BufferSearch.FindCrlf(work, pos);
                        if (crlf < 0)
                        {
                            goto stash;
                        }

                        var line = work[pos..crlf];
                        var semi = line.IndexOf((byte)';');
                        var sizeSpan = semi < 0 ? line : line[..semi];
                        if (!int.TryParse(Encoding.ASCII.GetString(sizeSpan),
                                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _currentChunkRemaining))
                        {
                            throw new HttpProtocolException("Invalid chunk size.");
                        }

                        pos = crlf + 2;
                        _phase = _currentChunkRemaining == 0 ? Phase.Trailer : Phase.ChunkData;
                        break;
                    }
                case Phase.ChunkData:
                    {
                        var avail = work.Length - pos;
                        var take = Math.Min(_currentChunkRemaining, avail);
                        if (take > 0)
                        {
                            _handle.Feed(work.Slice(pos, take));
                            _currentChunkRemaining -= take;
                            pos += take;
                        }

                        if (_currentChunkRemaining == 0)
                        {
                            _phase = Phase.ChunkDataCrlf;
                        }
                        else
                        {
                            goto stash;
                        }

                        break;
                    }
                case Phase.ChunkDataCrlf:
                    {
                        if (work.Length - pos < 2)
                        {
                            goto stash;
                        }

                        if (work[pos] != (byte)'\r' || work[pos + 1] != (byte)'\n')
                        {
                            throw new HttpProtocolException("Missing CRLF after chunk-data.");
                        }

                        pos += 2;
                        _phase = Phase.ChunkSize;
                        break;
                    }
                case Phase.Trailer:
                    {
                        var crlf = BufferSearch.FindCrlf(work, pos);
                        if (crlf < 0)
                        {
                            goto stash;
                        }

                        if (crlf == pos)
                        {
                            pos += 2;
                            _phase = Phase.Complete;
                            _handle.Complete();
                            _stashLen = 0;
                            consumed = pos - stashOffset;
                            if (consumed < 0)
                            {
                                consumed = 0;
                            }

                            return true;
                        }

                        pos = crlf + 2;
                        break;
                    }
            }
        }

    stash:
        var remaining = work.Length - pos;
        if (remaining > 0)
        {
            EnsureStash(remaining);
            work[pos..].CopyTo(_stash);
            _stashLen = remaining;
        }
        else
        {
            _stashLen = 0;
        }

        consumed = data.Length;
        return false;
    }

    private void EnsureStash(int needed)
    {
        if (_stash.Length < needed)
        {
            Array.Resize(ref _stash, Math.Max(needed, _stash.Length * 2 + 16));
        }
    }

    public bool OnEof()
    {
        if (_phase != Phase.Complete)
        {
            _handle.Abort(new HttpProtocolException("Connection closed mid-chunk."));
        }

        return _phase == Phase.Complete;
    }

    public HttpContent GetContent() => new StreamContent(_handle.AsStream());

    public void Dispose()
    {
        _handle.Dispose();
    }
}