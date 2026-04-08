namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// HTTP/3 error codes as defined in RFC 9114 §8.1.
/// These are used in GOAWAY frames and stream resets.
/// </summary>
public enum Http3ErrorCode : uint
{
    NoError = 0x100,
    GeneralProtocolError = 0x101,
    InternalError = 0x102,
    StreamCreationError = 0x103,
    ClosedCriticalStream = 0x104,
    FrameUnexpected = 0x105,
    FrameError = 0x106,
    ExcessiveLoad = 0x107,
    IdError = 0x108,
    SettingsError = 0x109,
    MissingSettings = 0x10a,
    RequestRejected = 0x10b,
    RequestCancelled = 0x10c,
    RequestIncomplete = 0x10d,
    MessageError = 0x10e,
    ConnectError = 0x10f,
    VersionFallback = 0x110,
}
