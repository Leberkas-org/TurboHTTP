namespace TurboHTTP.Protocol.Http2.Hpack;

/// <summary>
/// Represents a decoded HPACK header field.
/// NeverIndex = true means this header MUST NEVER be added to a dynamic table
/// (RFC 7541 §6.2.3). Applies to security-sensitive fields like Authorization,
/// Cookie, etc.
/// </summary>
internal readonly record struct HpackHeader(string Name, string Value, bool NeverIndex = false);