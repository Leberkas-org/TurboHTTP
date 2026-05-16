using System.Buffers;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased.Body;

internal static class BodyDecoderFactory
{
    public static IBodyDecoder Create(
        BodyClassification classification,
        long streamingThreshold,
        MemoryPool<byte> pool,
        long maxBodySize = 10_485_760)
    {
        switch (classification.Framing)
        {
            case BodyFraming.None:
                return new ContentLengthBufferedDecoder(0, pool);

            case BodyFraming.Length:
                {
                    var n = classification.ContentLength ?? 0;
                    if (n <= streamingThreshold)
                    {
                        return new ContentLengthBufferedDecoder((int)n, pool);
                    }

                    return new ContentLengthStreamedDecoder(n, maxBodySize);
                }

            case BodyFraming.Chunked:
                return new ChunkedBodyDecoder(maxBodySize);

            case BodyFraming.Close:
                return new CloseDelimitedBodyDecoder(maxBodySize);

            default:
                throw new ArgumentOutOfRangeException(nameof(classification));
        }
    }
}
