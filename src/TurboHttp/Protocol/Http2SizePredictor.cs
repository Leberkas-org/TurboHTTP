using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace TurboHttp.Protocol;

public sealed class Http2SizePredictor(bool useHuffman = true)
{
    private readonly HpackEncoder _encoder = new(useHuffman);

    public int Predict(HttpRequestMessage request, int maxFrameSize = 16384, int connectionWindow = int.MaxValue,
        int streamWindow = int.MaxValue)
    {
        var prediction = new Http2RequestPrediction();

        var headers = BuildHeaders(request);
        var headerBlock = _encoder.Encode(headers);

        prediction.HeaderBlockBytes = headerBlock.Length;

        var availableWindow = Math.Min(maxFrameSize, Math.Min(connectionWindow, streamWindow));

        // Headers + Continuation Frames
        prediction.FrameCount += PredictHeaderFrames(
            headerBlock.Length,
            maxFrameSize,
            ref prediction);

        // Body prediction
        if (request.Content is { } content)
        {
            var bodySize = GetContentLength(content);
            prediction.BodyBytes = (int)bodySize;

            prediction.FrameCount += PredictDataFrames(
                bodySize,
                availableWindow,
                ref prediction);
        }
        else
        {
            // Empty DATA frame for no content
            prediction.FrameOverheadBytes += 9;
            prediction.FrameCount += 1;
        }

        return prediction.HeaderBlockBytes + prediction.BodyBytes + prediction.FrameOverheadBytes;
    }

    private static long GetContentLength(HttpContent content)
    {
        // 1. Headers.ContentLength (preferred)
        if (content.Headers.ContentLength.HasValue)
        {
            return content.Headers.ContentLength.Value;
        }

        // 2. TryComputeLength (StreamContent etc.)
        _ = content.Headers.ContentLength ?? 0;

        return content.Headers.ContentLength!.Value;
    }

    private static int PredictHeaderFrames(
        int headerBlockSize,
        int maxFrameSize,
        ref Http2RequestPrediction prediction)
    {
        if (headerBlockSize == 0)
        {
            prediction.FrameOverheadBytes += 9; // Empty HEADERS frame
            return 1;
        }

        if (headerBlockSize <= maxFrameSize)
        {
            prediction.FrameOverheadBytes += 9; // 3 Byte length + 4 Byte stream ID + 1 Byte type + 1 Byte flags
            return 1;
        }

        var frames = (int)Math.Ceiling(headerBlockSize / (double)maxFrameSize);
        prediction.FrameOverheadBytes += frames * 9; // 1x HEADERS + (frames-1)x CONTINUATION
        prediction.FrameOverheadBytes += frames - 1; // END_HEADERS flag on first frame only

        return frames;
    }

    private static int PredictDataFrames(
        long bodySize,
        int maxChunkSize,
        ref Http2RequestPrediction prediction)
    {
        if (bodySize <= 0)
        {
            prediction.FrameOverheadBytes += 9;
            return 1;
        }

        var frames = (int)Math.Ceiling(bodySize / (double)maxChunkSize);
        prediction.FrameOverheadBytes += frames * 9; // DATA frame overhead
        return frames;
    }

    private static List<(string, string)> BuildHeaders(HttpRequestMessage request)
    {
        var list = new List<(string, string)>
        {
            (":method", request.Method.Method),
            (":path", request.RequestUri!.PathAndQuery),
            (":scheme", request.RequestUri.Scheme),
            (":authority", FormatAuthority(request.RequestUri))
        };

        // Filter HTTP/1.1 pseudo-headers (RFC 7540 §8.1.2.3)
        var forbiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "connection", "keep-alive", "proxy-connection", "transfer-encoding", "upgrade", "te"
        };

        // Request Headers
        foreach (var header in request.Headers)
        {
            var lowerName = header.Key.ToLowerInvariant();
            if (forbiddenHeaders.Contains(lowerName))
            {
                continue;
            }

            list.AddRange(header.Value.Select(value => (lowerName, value)));
        }

        // Content Headers
        if (request.Content?.Headers == null) return list;
        foreach (var header in request.Content.Headers)
        {
            var lowerName = header.Key.ToLowerInvariant();
            if (forbiddenHeaders.Contains(lowerName))
            {
                continue;
            }

            list.AddRange(header.Value.Select(value => (lowerName, value)));
        }

        return list;
    }

    private static string FormatAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
}

public sealed class Http2RequestPrediction
{
    public int HeaderBlockBytes { get; set; }
    public int FrameOverheadBytes { get; set; }
    public int BodyBytes { get; set; }
    public int FrameCount { get; set; }
}