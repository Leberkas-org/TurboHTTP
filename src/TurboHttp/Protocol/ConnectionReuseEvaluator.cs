using System;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9112 §9 — Evaluates whether an HTTP connection can be reused after receiving a response.
/// This is pure protocol logic with no I/O or side effects.
/// </summary>
public static class ConnectionReuseEvaluator
{
    /// <summary>
    /// Determines whether the underlying TCP connection can be reused after this response.
    /// </summary>
    /// <param name="response">The HTTP response that was decoded.</param>
    /// <param name="httpVersion">
    ///     The HTTP version negotiated for this connection.
    ///     Use <see cref="HttpVersion.Version10"/>, <see cref="HttpVersion.Version11"/>,
    ///     or <see cref="HttpVersion.Version20"/>.
    /// </param>
    /// <param name="bodyFullyConsumed">
    ///     True if the response body was fully consumed (all declared bytes read).
    ///     False if decoding stopped early (e.g. <c>NeedMoreData</c> or caller abandoned read).
    ///     If false, the connection must be closed to avoid framing desync.
    ///     Default is true.
    /// </param>
    /// <param name="protocolErrorOccurred">
    ///     True if a protocol-level error was detected during decoding of this response.
    ///     Protocol errors always require connection close because the decoder state is unknown.
    ///     Default is false.
    /// </param>
    /// <returns>
    ///     A <see cref="ConnectionReuseDecision"/> indicating whether the connection
    ///     can be reused, the reason for the decision, and any Keep-Alive parameters.
    /// </returns>
    public static ConnectionReuseDecision Evaluate(
        HttpResponseMessage response,
        Version httpVersion,
        bool bodyFullyConsumed = true,
        bool protocolErrorOccurred = false)
    {
        // HTTP/2: connections are multiplexed at the stream layer.
        // Stream-level errors do not affect the connection. The I/O layer (not this evaluator)
        // handles connection-level GOAWAY and RST_STREAM events.
        // RFC 9113 §5.1: stream close != connection close.
        if (httpVersion == HttpVersion.Version20)
        {
            return ConnectionReuseDecision.KeepAlive(
                "RFC 9113 §5.1: HTTP/2 connections are multiplexed; stream close does not affect connection reuse.");
        }

        // Protocol error: connection state is unknown; must close.
        // RFC 9112 §9.6: if any error occurs, close the connection.
        if (protocolErrorOccurred)
        {
            return ConnectionReuseDecision.Close(
                "RFC 9112 §9.6: Protocol error occurred during decoding; connection state is unknown.");
        }

        // Body not fully consumed: framing integrity requires close.
        // RFC 9112 §9.6: if message body is not consumed, the server's write position
        // is unknown and subsequent requests would read stale data.
        if (!bodyFullyConsumed)
        {
            return ConnectionReuseDecision.Close(
                "RFC 9112 §9.6: Response body not fully consumed; closing to preserve framing integrity.");
        }

        // 101 Switching Protocols: the connection has been upgraded to another protocol.
        // It cannot be returned to an HTTP connection pool.
        if ((int)response.StatusCode == 101)
        {
            return ConnectionReuseDecision.Close(
                "RFC 9112 §9.6: 101 Switching Protocols — connection upgraded; cannot reuse for HTTP.");
        }

        // Explicit Connection: close — server requested close regardless of version.
        // RFC 9110 §7.6.1: Connection header tokens are case-insensitive.
        if (HasConnectionToken(response, "close"))
        {
            return ConnectionReuseDecision.Close(
                "RFC 9112 §9.6: Server sent 'Connection: close'.");
        }

        // HTTP/1.0: persistent connections are opt-in (RFC 9112 §9.3).
        // Reuse only when server explicitly sent Connection: Keep-Alive.
        if (httpVersion == HttpVersion.Version10)
        {
            if (HasConnectionToken(response, "keep-alive"))
            {
                var (timeout, maxRequests) = ParseKeepAliveParameters(response);
                return ConnectionReuseDecision.KeepAlive(
                    "RFC 9112 §9.3: HTTP/1.0 with explicit Connection: Keep-Alive.",
                    timeout,
                    maxRequests);
            }

            return ConnectionReuseDecision.Close(
                "RFC 9112 §9.3: HTTP/1.0 connections are not persistent by default.");
        }

        // HTTP/1.1: persistent connections are the default (RFC 9112 §9.3).
        var (timeout2, maxRequests2) = ParseKeepAliveParameters(response);
        return ConnectionReuseDecision.KeepAlive(
            "RFC 9112 §9.3: HTTP/1.1 persistent connection (default).",
            timeout2,
            maxRequests2);
    }

    // ── Private Helpers ──────────────────────────────────────────────────────────

    private static bool HasConnectionToken(HttpResponseMessage response, string token)
    {
        return response.Headers.Connection.Any(t => t.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static (TimeSpan? timeout, int? maxRequests) ParseKeepAliveParameters(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Keep-Alive", out var values))
        {
            return (null, null);
        }

        TimeSpan? timeout = null;
        int? maxRequests = null;

        foreach (var headerValue in values)
        {
            // Keep-Alive: timeout=30, max=100
            foreach (var param in headerValue.Split(','))
            {
                var trimmed = param.Trim();
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx < 0)
                {
                    continue;
                }

                var key = trimmed[..eqIdx].Trim();
                var val = trimmed[(eqIdx + 1)..].Trim();

                if (key.Equals("timeout", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var seconds))
                {
                    timeout = TimeSpan.FromSeconds(seconds);
                }
                else if (key.Equals("max", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var max))
                {
                    maxRequests = max;
                }
            }
        }

        return (timeout, maxRequests);
    }
}