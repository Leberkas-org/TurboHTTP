using System.Net;
using System.Net.Sockets;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Sends an HTTP/1.0 request to a real TCP endpoint using <see cref="Http10Encoder"/>
/// and decodes the response with <see cref="Http10Decoder"/>.
/// Each call opens a new TCP connection (HTTP/1.0 default: no keep-alive).
/// </summary>
public static class Http10Helper
{
    private const int EncodeBufferSize = 2 * 1024 * 1024; // 2 MB — large enough for any test body
    private const int ReadChunkSize = 65536; // 64 KB read chunks
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Encodes <paramref name="request"/> with the HTTP/1.0 encoder, sends it over a new
    /// TCP connection to 127.0.0.1:<paramref name="port"/>, reads bytes until the decoder
    /// produces a complete response (or EOF), and returns the decoded response.
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(
        int port,
        HttpRequestMessage request,
        CancellationToken externalCt = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        linked.CancelAfter(DefaultTimeout);
        var ct = linked.Token;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, ct);

        var stream = tcp.GetStream();

        // Encode the HTTP/1.0 request into bytes
        var encodeBuffer = new Memory<byte>(new byte[EncodeBufferSize]);
        var written = Http10Encoder.Encode(request, ref encodeBuffer);

        // Send the encoded bytes
        await stream.WriteAsync(encodeBuffer[..written], ct);

        // Read response bytes and feed into decoder
        var decoder = new Http10Decoder();
        var readBuffer = new byte[ReadChunkSize];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, ct);

            if (bytesRead == 0)
            {
                // Server closed the connection — try to decode whatever was buffered
                if (decoder.TryDecodeEof(out var eofResponse))
                {
                    return eofResponse!;
                }

                throw new InvalidOperationException(
                    "Server closed connection before a complete HTTP/1.0 response was received.");
            }

            // Copy to a fresh array so the decoder's stored _remainder does not alias
            // readBuffer. Http10Decoder.Combine returns the incoming slice directly when
            // its internal remainder is empty, meaning _remainder ends up pointing into
            // readBuffer. The next ReadAsync call overwrites readBuffer, corrupting the
            // stored remainder and causing large (multi-read) responses to fail.
            var chunk = readBuffer.AsMemory(0, bytesRead).ToArray();
            if (decoder.TryDecode(chunk, out var response))
            {
                return response!;
            }
        }
    }

    /// <summary>
    /// Convenience overload that builds a GET request and calls <see cref="SendAsync"/>.
    /// </summary>
    public static Task<HttpResponseMessage> GetAsync(int port, string path, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(port, path));
        return SendAsync(port, request, ct);
    }

    /// <summary>
    /// Convenience overload that builds a HEAD request and calls <see cref="SendAsync"/>.
    /// HEAD responses have no body; the connection closes and <see cref="Http10Decoder.TryDecodeEof"/> is used.
    /// </summary>
    public static Task<HttpResponseMessage> HeadAsync(int port, string path, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, BuildUri(port, path));
        return SendAsync(port, request, ct);
    }

    private static Uri BuildUri(int port, string path)
        => new($"http://127.0.0.1:{port}{path}");
}