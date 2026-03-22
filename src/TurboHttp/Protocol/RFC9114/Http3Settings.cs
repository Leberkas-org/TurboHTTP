using System;
using System.Collections.Generic;
using TurboHttp.Protocol.RFC9000;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 Settings  —  RFC 9114 §7.2.4
//
// SETTINGS parameters are conveyed in a SETTINGS frame on the control stream.
// Each parameter is an identifier-value pair encoded as QUIC variable-length
// integers. Unlike HTTP/2, identifiers use the same space but different
// semantics — HTTP/2 settings MUST NOT appear in HTTP/3 (§7.2.4.1).
// Unknown settings MUST be ignored (extension tolerance).

/// <summary>
/// Well-known HTTP/3 setting identifiers per RFC 9114 §7.2.4.1.
/// </summary>
public static class Http3SettingId
{
    /// <summary>
    /// SETTINGS_QPACK_MAX_TABLE_CAPACITY (RFC 9204 §5).
    /// Maximum size the QPACK dynamic table can reach. Default: 0.
    /// </summary>
    public const long QpackMaxTableCapacity = 0x01;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_ENABLE_PUSH.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2EnablePush = 0x02;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_MAX_CONCURRENT_STREAMS.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2MaxConcurrentStreams = 0x03;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_INITIAL_WINDOW_SIZE.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2InitialWindowSize = 0x04;

    /// <summary>
    /// Reserved identifier — corresponds to HTTP/2 SETTINGS_MAX_FRAME_SIZE.
    /// MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public const long ReservedH2MaxFrameSize = 0x05;

    /// <summary>
    /// SETTINGS_MAX_FIELD_SECTION_SIZE (RFC 9114 §7.2.4.1).
    /// Advisory maximum size of a header block the peer is prepared to accept.
    /// </summary>
    public const long MaxFieldSectionSize = 0x06;

    /// <summary>
    /// SETTINGS_QPACK_BLOCKED_STREAMS (RFC 9204 §5).
    /// Maximum number of streams that can be blocked waiting for QPACK. Default: 0.
    /// </summary>
    public const long QpackBlockedStreams = 0x07;

    /// <summary>
    /// Returns true if the identifier is reserved (corresponds to an HTTP/2 setting
    /// that MUST NOT be sent in HTTP/3 per RFC 9114 §7.2.4.1).
    /// </summary>
    public static bool IsReservedH2Setting(long identifier) =>
        identifier is ReservedH2EnablePush
            or ReservedH2MaxConcurrentStreams
            or ReservedH2InitialWindowSize
            or ReservedH2MaxFrameSize;

    /// <summary>
    /// Validates that a list of setting parameters does not contain HTTP/2-specific
    /// identifiers (RFC 9114 §7.2.4.1). This can be used for pre-validation of
    /// raw payloads before deserialization.
    /// </summary>
    /// <param name="parameters">The setting identifier-value pairs to validate.</param>
    /// <exception cref="Http3SettingsException">
    /// Thrown if any parameter uses a reserved HTTP/2 identifier.
    /// </exception>
    public static void RejectForbiddenH2Settings(IReadOnlyList<(long Identifier, long Value)> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var (id, _) = parameters[i];
            if (IsReservedH2Setting(id))
            {
                throw new Http3SettingsException(
                    $"Setting identifier 0x{id:x2} is a reserved HTTP/2 setting and MUST NOT appear in HTTP/3 (RFC 9114 §7.2.4.1).");
            }
        }
    }
}

/// <summary>
/// Represents a collection of HTTP/3 SETTINGS parameters (RFC 9114 §7.2.4).
/// Preserves unknown (extension) settings alongside well-known ones.
/// </summary>
public sealed class Http3Settings
{
    private readonly Dictionary<long, long> _parameters = new();

    /// <summary>
    /// Gets the value for the given setting identifier, or <c>null</c> if not present.
    /// </summary>
    public long? this[long identifier] =>
        _parameters.TryGetValue(identifier, out var value) ? value : null;

    /// <summary>
    /// SETTINGS_MAX_FIELD_SECTION_SIZE (0x06).
    /// Advisory maximum size of a header block the peer is prepared to accept.
    /// <c>null</c> means the setting was not received (no limit imposed).
    /// </summary>
    public long? MaxFieldSectionSize => this[Http3SettingId.MaxFieldSectionSize];

    /// <summary>
    /// SETTINGS_QPACK_MAX_TABLE_CAPACITY (0x01). Default: 0.
    /// </summary>
    public long QpackMaxTableCapacity => this[Http3SettingId.QpackMaxTableCapacity] ?? 0;

    /// <summary>
    /// SETTINGS_QPACK_BLOCKED_STREAMS (0x07). Default: 0.
    /// </summary>
    public long QpackBlockedStreams => this[Http3SettingId.QpackBlockedStreams] ?? 0;

    /// <summary>
    /// All parameters (known and unknown), for extension tolerance.
    /// </summary>
    public IReadOnlyDictionary<long, long> AllParameters => _parameters;

    /// <summary>
    /// Sets a parameter. Overwrites any existing value for the identifier.
    /// Throws if the identifier is a reserved HTTP/2 setting that MUST NOT
    /// be sent in HTTP/3 (RFC 9114 §7.2.4.1).
    /// </summary>
    public void Set(long identifier, long value)
    {
        if (Http3SettingId.IsReservedH2Setting(identifier))
        {
            throw new Http3SettingsException(
                $"Setting identifier 0x{identifier:x2} is reserved (HTTP/2 setting) and MUST NOT be sent in HTTP/3 (RFC 9114 §7.2.4.1).");
        }

        _parameters[identifier] = value;
    }

    /// <summary>
    /// Serializes all parameters into a SETTINGS frame payload (identifier-value pairs
    /// encoded as QUIC variable-length integers).
    /// </summary>
    public byte[] Serialize()
    {
        var size = 0;
        foreach (var (id, val) in _parameters)
        {
            size += QuicVarInt.EncodedLength(id) + QuicVarInt.EncodedLength(val);
        }

        var buf = new byte[size];
        var span = buf.AsSpan();

        foreach (var (id, val) in _parameters)
        {
            var written = QuicVarInt.Encode(id, span);
            span = span[written..];
            written = QuicVarInt.Encode(val, span);
            span = span[written..];
        }

        return buf;
    }

    /// <summary>
    /// Deserializes a SETTINGS frame payload (sequence of identifier-value QUIC varint pairs).
    /// Unknown identifiers are preserved. Reserved HTTP/2 identifiers cause a
    /// <see cref="Http3SettingsException"/> (RFC 9114 §7.2.4.1).
    /// Duplicate identifiers cause a <see cref="Http3SettingsException"/> (RFC 9114 §7.2.4).
    /// </summary>
    public static Http3Settings Deserialize(ReadOnlySpan<byte> payload)
    {
        var settings = new Http3Settings();

        while (payload.Length > 0)
        {
            if (!QuicVarInt.TryDecode(payload, out var identifier, out var consumed))
            {
                throw new Http3SettingsException("Incomplete setting identifier in SETTINGS payload.");
            }

            payload = payload[consumed..];

            if (!QuicVarInt.TryDecode(payload, out var value, out consumed))
            {
                throw new Http3SettingsException("Incomplete setting value in SETTINGS payload.");
            }

            payload = payload[consumed..];

            if (Http3SettingId.IsReservedH2Setting(identifier))
            {
                throw new Http3SettingsException(
                    $"Setting identifier 0x{identifier:x2} is reserved (HTTP/2 setting) and MUST NOT appear in HTTP/3 (RFC 9114 §7.2.4.1).");
            }

            if (settings._parameters.ContainsKey(identifier))
            {
                throw new Http3SettingsException(
                    $"Duplicate setting identifier 0x{identifier:x2} in SETTINGS payload (RFC 9114 §7.2.4).");
            }

            settings._parameters[identifier] = value;
        }

        return settings;
    }

    /// <summary>
    /// Creates an <see cref="Http3SettingsFrame"/> from these settings.
    /// </summary>
    public Http3SettingsFrame ToFrame()
    {
        var parameters = new List<(long, long)>(_parameters.Count);
        foreach (var (id, val) in _parameters)
        {
            parameters.Add((id, val));
        }

        return new Http3SettingsFrame(parameters);
    }
}

/// <summary>
/// Thrown when an HTTP/3 SETTINGS violation is detected (RFC 9114 §7.2.4).
/// </summary>
public sealed class Http3SettingsException : Exception
{
    public Http3SettingsException(string message) : base(message) { }
    public Http3SettingsException(string message, Exception innerException) : base(message, innerException) { }
}
