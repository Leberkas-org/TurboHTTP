using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

/// <summary>
/// Predicts the wire size of an HTTP request for HTTP/1.0, HTTP/1.1 and HTTP/2.
/// </summary>
public static class HttpSizePredictor
{
    /// <summary>
    /// Predicts the total byte size for the given request, based on request.Version.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static int Predict(HttpRequestMessage request, int maxFrameSize = 16384, int connectionWindow = int.MaxValue,
        int streamWindow = int.MaxValue)
    {
        var version = request.Version;

        return version switch
        {
            _ when version == HttpVersion.Version10 => PredictHttp10(request),
            _ when version == HttpVersion.Version11 => PredictHttp11(request),
            _ when version == HttpVersion.Version20 => PredictHttp2(request, maxFrameSize, connectionWindow,
                streamWindow),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static int PredictHttp10(HttpRequestMessage request)
        => PredictHttp1x(request, version: "1.0", requireHost: false);

    private static int PredictHttp11(HttpRequestMessage request)
        => PredictHttp1x(request, version: "1.1", requireHost: true);

    private static int PredictHttp2(HttpRequestMessage request, int maxFrameSize = 16384,
        int connectionWindow = int.MaxValue,
        int streamWindow = int.MaxValue,
        bool useHuffman = true)
    {
        var encoder = new HpackEncoder(useHuffman);
        var prediction = new Http2RequestPrediction();

        var headers = BuildHttp2Headers(request);
        var headerBlock = encoder.Encode(headers);
        prediction.HeaderBlockBytes = headerBlock.Length;

        var availableWindow = Math.Min(maxFrameSize, Math.Min(connectionWindow, streamWindow));

        prediction.FrameCount += PredictHeaderFrames(headerBlock.Length, maxFrameSize, ref prediction);

        if (request.Content is { } content)
        {
            var bodySize = GetContentLength(content);
            prediction.BodyBytes = (int)bodySize;
            prediction.FrameCount += PredictDataFrames(bodySize, availableWindow, ref prediction);
        }
        else
        {
            prediction.FrameOverheadBytes += 9;
            prediction.FrameCount += 1;
        }

        return prediction.HeaderBlockBytes + prediction.BodyBytes + prediction.FrameOverheadBytes;
    }

    private static int PredictHttp1x(HttpRequestMessage request, string version, bool requireHost)
    {
        var prediction = new Http11RequestPrediction();

        var requestLine = $"{request.Method.Method} {request.RequestUri!.PathAndQuery} HTTP/{version}\r\n";
        prediction.RequestLineBytes = Encoding.ASCII.GetByteCount(requestLine);

        if (requireHost)
        {
            var hostHeader = $"Host: {FormatAuthority(request.RequestUri)}\r\n";
            prediction.HeaderBytes += Encoding.ASCII.GetByteCount(hostHeader);
        }

        var forbiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "host" };

        foreach (var header in request.Headers)
        {
            if (forbiddenHeaders.Contains(header.Key)) continue;
            foreach (var value in header.Value)
            {
                prediction.HeaderBytes += Encoding.ASCII.GetByteCount($"{header.Key}: {value}\r\n");
            }
        }

        if (request.Content is { } content)
        {
            foreach (var header in content.Headers)
            {
                foreach (var value in header.Value)
                {
                    prediction.HeaderBytes += Encoding.ASCII.GetByteCount($"{header.Key}: {value}\r\n");
                }
            }

            prediction.BodyBytes = (int)GetContentLength(content);
        }

        prediction.HeaderTerminatorBytes = 2; // \r\n

        return prediction.RequestLineBytes
               + prediction.HeaderBytes
               + prediction.HeaderTerminatorBytes
               + prediction.BodyBytes;
    }

    private static int PredictHeaderFrames(int headerBlockSize, int maxFrameSize, ref Http2RequestPrediction prediction)
    {
        if (headerBlockSize == 0 || headerBlockSize <= maxFrameSize)
        {
            prediction.FrameOverheadBytes += 9;
            return 1;
        }

        var frames = (int)Math.Ceiling(headerBlockSize / (double)maxFrameSize);
        prediction.FrameOverheadBytes += frames * 9;
        prediction.FrameOverheadBytes += frames - 1;
        return frames;
    }

    private static int PredictDataFrames(long bodySize, int maxChunkSize, ref Http2RequestPrediction prediction)
    {
        if (bodySize <= 0)
        {
            prediction.FrameOverheadBytes += 9;
            return 1;
        }

        var frames = (int)Math.Ceiling(bodySize / (double)maxChunkSize);
        prediction.FrameOverheadBytes += frames * 9;
        return frames;
    }

    private static List<(string, string)> BuildHttp2Headers(HttpRequestMessage request)
    {
        var list = new List<(string, string)>
        {
            (":method", request.Method.Method),
            (":path", request.RequestUri!.PathAndQuery),
            (":scheme", request.RequestUri.Scheme),
            (":authority", FormatAuthority(request.RequestUri))
        };

        var forbiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "connection", "keep-alive", "proxy-connection", "transfer-encoding", "upgrade", "te"
        };

        foreach (var header in request.Headers)
        {
            var lower = header.Key.ToLowerInvariant();
            if (forbiddenHeaders.Contains(lower)) continue;
            list.AddRange(header.Value.Select(value => (lower, value)));
        }

        if (request.Content?.Headers is null) return list;

        foreach (var header in request.Content.Headers)
        {
            var lower = header.Key.ToLowerInvariant();
            if (forbiddenHeaders.Contains(lower)) continue;
            list.AddRange(header.Value.Select(value => (lower, value)));
        }

        return list;
    }

    private static long GetContentLength(HttpContent content)
    {
        if (content.Headers.ContentLength.HasValue)
            return content.Headers.ContentLength.Value;

        _ = content.Headers.ContentLength ?? 0;
        return content.Headers.ContentLength!.Value;
    }

    private static string FormatAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
}

public interface IRequestPrediction
{
    int Total { get; }
}

public sealed class Http11RequestPrediction : IRequestPrediction
{
    public int RequestLineBytes { get; set; }
    public int HeaderBytes { get; set; }
    public int HeaderTerminatorBytes { get; set; }
    public int BodyBytes { get; set; }

    public int Total => RequestLineBytes + HeaderBytes + HeaderTerminatorBytes + BodyBytes;
}

public sealed class Http2RequestPrediction : IRequestPrediction
{
    public int HeaderBlockBytes { get; set; }
    public int FrameOverheadBytes { get; set; }
    public int BodyBytes { get; set; }
    public int FrameCount { get; set; }
    public int Total => HeaderBlockBytes + FrameOverheadBytes + BodyBytes;
}