using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 PUSH_PROMISE Validation  —  RFC 9114 §4.6, §7.2.5
//
// A server uses PUSH_PROMISE to pre-emptively send request-response pairs to
// a client it anticipates will need.  The client validates:
//
// Key rules:
//   - The push ID MUST NOT exceed the client's MAX_PUSH_ID limit (§7.2.7).
//   - Each push ID MUST be used at most once; a duplicate is a connection
//     error of type H3_ID_ERROR (§4.6).
//   - Promised request headers MUST include :method, :scheme, :path
//     pseudo-headers (§4.3.1) and MUST NOT include :status (§4.6).
//   - The :method MUST be safe and cacheable (§4.6) — i.e. GET or HEAD.
//   - Connection-specific headers are forbidden (§4.2).
//   - PUSH_PROMISE MUST only be received on a request stream, not the
//     control stream (§7.2.5).

/// <summary>
/// Validates incoming PUSH_PROMISE frames from the server per RFC 9114 §4.6, §7.2.5.
/// Tracks used push IDs to detect duplicates and validates promised request headers.
/// </summary>
public sealed class Http3PushPromiseValidator
{
    private readonly Http3MaxPushIdHandler _maxPushIdHandler;
    private readonly HashSet<long> _usedPushIds = new();

    /// <summary>
    /// Creates a new PUSH_PROMISE validator that delegates push ID range checks
    /// to the given <see cref="Http3MaxPushIdHandler"/>.
    /// </summary>
    public Http3PushPromiseValidator(Http3MaxPushIdHandler maxPushIdHandler)
    {
        _maxPushIdHandler = maxPushIdHandler ?? throw new ArgumentNullException(nameof(maxPushIdHandler));
    }

    /// <summary>
    /// The set of push IDs that have been used by PUSH_PROMISE frames so far.
    /// </summary>
    public int UsedPushIdCount => _usedPushIds.Count;

    /// <summary>
    /// Returns whether the given push ID has already been used in a PUSH_PROMISE.
    /// </summary>
    public bool IsPushIdUsed(long pushId) => _usedPushIds.Contains(pushId);

    /// <summary>
    /// Validates a PUSH_PROMISE frame from the server.
    /// Checks push ID range, duplicate detection, and promised header validity.
    /// </summary>
    /// <param name="frame">The PUSH_PROMISE frame to validate.</param>
    /// <param name="promisedHeaders">
    /// The decoded header list from the PUSH_PROMISE header block.
    /// Must include pseudo-headers (:method, :scheme, :path).
    /// </param>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if the push ID exceeds
    /// the MAX_PUSH_ID limit or is a duplicate.
    /// Thrown with <see cref="Http3ErrorCode.MessageError"/> if promised headers
    /// are invalid.
    /// </exception>
    public void Validate(Http3PushPromiseFrame frame, IReadOnlyList<(string Name, string Value)> promisedHeaders)
    {
        ValidatePushId(frame.PushId);
        ValidatePromisedHeaders(promisedHeaders);
    }

    /// <summary>
    /// Validates only the push ID portion: range check and duplicate detection.
    /// </summary>
    /// <param name="pushId">The push ID from the PUSH_PROMISE frame.</param>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if the push ID is out
    /// of range or has already been used.
    /// </exception>
    public void ValidatePushId(long pushId)
    {
        // RFC 9114 §7.2.7: push ID must be within MAX_PUSH_ID limit
        _maxPushIdHandler.ValidatePushId(pushId);

        // RFC 9114 §4.6: "each push ID MUST only be used once"
        if (!_usedPushIds.Add(pushId))
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"Duplicate push ID {pushId} in PUSH_PROMISE (RFC 9114 §4.6).");
        }
    }

    /// <summary>
    /// Validates the promised request headers per RFC 9114 §4.6 and §4.3.1.
    /// Promised requests MUST include :method, :scheme, :path and MUST NOT
    /// include :status.  The :method MUST be safe and cacheable (GET or HEAD).
    /// Connection-specific headers are also forbidden (§4.2).
    /// </summary>
    /// <param name="headers">The decoded header list from the PUSH_PROMISE.</param>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.MessageError"/> if the promised
    /// headers violate any constraint.
    /// </exception>
    public static void ValidatePromisedHeaders(IReadOnlyList<(string Name, string Value)> headers)
    {
        string? method = null;
        string? scheme = null;
        string? path = null;
        var hasStatus = false;

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];

            if (name == ":method")
            {
                if (method is not null)
                {
                    throw new Http3Exception(Http3ErrorCode.MessageError,
                        "RFC 9114 §4.3.1: Duplicate :method pseudo-header in PUSH_PROMISE");
                }

                method = value;
            }
            else if (name == ":scheme")
            {
                if (scheme is not null)
                {
                    throw new Http3Exception(Http3ErrorCode.MessageError,
                        "RFC 9114 §4.3.1: Duplicate :scheme pseudo-header in PUSH_PROMISE");
                }

                scheme = value;
            }
            else if (name == ":path")
            {
                if (path is not null)
                {
                    throw new Http3Exception(Http3ErrorCode.MessageError,
                        "RFC 9114 §4.3.1: Duplicate :path pseudo-header in PUSH_PROMISE");
                }

                path = value;
            }
            else if (name == ":status")
            {
                hasStatus = true;
            }
            else if (name.Length > 0 && name[0] != ':')
            {
                // Validate regular headers via Http3FieldValidator
                Http3FieldValidator.ValidateFieldName(name);
                Http3FieldValidator.ValidateConnectionSpecific(name, value);
            }
        }

        // RFC 9114 §4.6: PUSH_PROMISE MUST NOT include :status
        if (hasStatus)
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.6: PUSH_PROMISE MUST NOT contain :status pseudo-header");
        }

        // RFC 9114 §4.3.1: :method is required
        if (method is null)
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.3.1: PUSH_PROMISE missing required :method pseudo-header");
        }

        // RFC 9114 §4.3.1: :scheme is required
        if (scheme is null)
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.3.1: PUSH_PROMISE missing required :scheme pseudo-header");
        }

        // RFC 9114 §4.3.1: :path is required
        if (path is null)
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                "RFC 9114 §4.3.1: PUSH_PROMISE missing required :path pseudo-header");
        }

        // RFC 9114 §4.6: promised method MUST be safe and cacheable
        // RFC 9110 §9.2.1 safe methods: GET, HEAD, OPTIONS, TRACE
        // RFC 9110 §9.2.3 cacheable methods: GET, HEAD, POST
        // Intersection (safe + cacheable): GET, HEAD
        if (!string.Equals(method, "GET", StringComparison.Ordinal) &&
            !string.Equals(method, "HEAD", StringComparison.Ordinal))
        {
            throw new Http3Exception(Http3ErrorCode.MessageError,
                $"RFC 9114 §4.6: PUSH_PROMISE method '{method}' is not safe and cacheable; only GET and HEAD are allowed");
        }
    }
}
