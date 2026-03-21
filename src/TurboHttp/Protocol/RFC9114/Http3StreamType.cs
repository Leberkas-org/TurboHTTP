namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// HTTP/3 unidirectional stream types as defined in RFC 9114 §6.2.
/// The stream type is sent as the first bytes on a unidirectional stream
/// to indicate the purpose of that stream.
/// </summary>
public enum Http3StreamType : long
{
    /// <summary>
    /// Control stream (RFC 9114 §6.2.1). Each side MUST initiate exactly one
    /// control stream. SETTINGS is the first frame sent on this stream.
    /// </summary>
    Control = 0x00,

    /// <summary>
    /// Push stream (RFC 9114 §6.2.2). Initiated by the server to fulfill
    /// a previously promised server push. Carries Push ID as a prefix.
    /// </summary>
    Push = 0x01,

    /// <summary>
    /// QPACK encoder stream (RFC 9204 §4.2). Carries QPACK encoder
    /// instructions for dynamic table updates.
    /// </summary>
    QpackEncoder = 0x02,

    /// <summary>
    /// QPACK decoder stream (RFC 9204 §4.2). Carries QPACK decoder
    /// instructions (acknowledgements, cancellations).
    /// </summary>
    QpackDecoder = 0x03,
}
