using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TurboHttp.Client;

/// <summary>
/// The primary TurboHttp client interface. Provides a channel-based API for high-throughput
/// request/response streaming and a conventional <see cref="SendAsync"/> method for single-request use.
/// </summary>
public interface ITurboHttpClient
{
    /// <summary>Gets or sets the base address used to resolve relative request URIs.</summary>
    Uri? BaseAddress { get; set; }

    /// <summary>Default headers sent with every request.</summary>
    HttpRequestHeaders DefaultRequestHeaders { get; }

    /// <summary>Gets or sets the default HTTP version for new requests (defaults to HTTP/1.1).</summary>
    Version DefaultRequestVersion { get; set; }

    /// <summary>Gets or sets the policy that determines which HTTP version is used when negotiating with the server.</summary>
    HttpVersionPolicy DefaultVersionPolicy { get; set; }

    /// <summary>Gets or sets the timeout applied to each <see cref="SendAsync"/> call.</summary>
    TimeSpan Timeout { get; set; }

    /// <summary>Gets or sets the maximum number of bytes to buffer in the response content.</summary>
    long MaxResponseContentBufferSize { get; set; }

    /// <summary>
    /// Channel endpoint for writing requests directly into the pipeline.
    /// Use for high-throughput scenarios where multiple requests are submitted concurrently
    /// without waiting for individual responses. Pair with <see cref="Responses"/> to read responses.
    /// </summary>
    ChannelWriter<HttpRequestMessage> Requests { get; }

    /// <summary>
    /// Channel endpoint for reading responses produced by the pipeline.
    /// Each response's <see cref="HttpResponseMessage.RequestMessage"/> identifies the originating request.
    /// </summary>
    ChannelReader<HttpResponseMessage> Responses { get; }

    /// <summary>Cancels all in-flight requests and clears the pending request map.</summary>
    void CancelPendingRequests();

    /// <summary>
    /// Sends <paramref name="request"/> and returns the response.
    /// Internally writes to <see cref="Requests"/> and awaits the matching response.
    /// The call times out after <see cref="Timeout"/>.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}