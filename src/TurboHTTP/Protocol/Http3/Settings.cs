namespace TurboHTTP.Protocol.Http3;

// HTTP/3 Settings  —  RFC 9114 §7.2.4
//
// SETTINGS parameters are conveyed in a SETTINGS frame on the control stream.
// Each parameter is an identifier-value pair encoded as QUIC variable-length
// integers. Unlike HTTP/2, identifiers use the same space but different
// semantics — HTTP/2 settings MUST NOT appear in HTTP/3 (§7.2.4.1).
// Unknown settings MUST be ignored (extension tolerance).

/// <summary>
/// Represents a collection of HTTP/3 SETTINGS parameters (RFC 9114 §7.2.4).
/// Preserves unknown (extension) settings alongside well-known ones.
/// </summary>
internal sealed class Settings
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
    public long? MaxFieldSectionSize => this[SettingsIdentifier.MaxFieldSectionSize];

    /// <summary>
    /// SETTINGS_QPACK_MAX_TABLE_CAPACITY (0x01). Default: 0.
    /// </summary>
    public long QpackMaxTableCapacity => this[SettingsIdentifier.QpackMaxTableCapacity] ?? 0;

    /// <summary>
    /// SETTINGS_QPACK_BLOCKED_STREAMS (0x07). Default: 0.
    /// </summary>
    public long QpackBlockedStreams => this[SettingsIdentifier.QpackBlockedStreams] ?? 0;

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
        if (SettingsIdentifier.IsReservedH2Setting(identifier))
        {
            throw new Http3Exception(ErrorCode.SettingsError,
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
    /// <see cref="Http3Exception"/> (RFC 9114 §7.2.4.1).
    /// Duplicate identifiers cause a <see cref="Http3Exception"/> (RFC 9114 §7.2.4).
    /// </summary>
    public static Settings Deserialize(ReadOnlySpan<byte> payload)
    {
        var settings = new Settings();

        while (payload.Length > 0)
        {
            if (!QuicVarInt.TryDecode(payload, out var identifier, out var consumed))
            {
                throw new Http3Exception(ErrorCode.SettingsError, "Incomplete setting identifier in SETTINGS payload.");
            }

            payload = payload[consumed..];

            if (!QuicVarInt.TryDecode(payload, out var value, out consumed))
            {
                throw new Http3Exception(ErrorCode.SettingsError, "Incomplete setting value in SETTINGS payload.");
            }

            payload = payload[consumed..];

            if (SettingsIdentifier.IsReservedH2Setting(identifier))
            {
                throw new Http3Exception(ErrorCode.SettingsError,
                    $"Setting identifier 0x{identifier:x2} is reserved (HTTP/2 setting) and MUST NOT appear in HTTP/3 (RFC 9114 §7.2.4.1).");
            }

            if (settings._parameters.ContainsKey(identifier))
            {
                throw new Http3Exception(ErrorCode.SettingsError,
                    $"Duplicate setting identifier 0x{identifier:x2} in SETTINGS payload (RFC 9114 §7.2.4).");
            }

            settings._parameters[identifier] = value;
        }

        return settings;
    }

    /// <summary>
    /// Creates an <see cref="SettingsFrame"/> from these settings.
    /// </summary>
    public SettingsFrame ToFrame()
    {
        var parameters = new List<(long, long)>(_parameters.Count);
        foreach (var (id, val) in _parameters)
        {
            parameters.Add((id, val));
        }

        return new SettingsFrame(parameters);
    }
}
