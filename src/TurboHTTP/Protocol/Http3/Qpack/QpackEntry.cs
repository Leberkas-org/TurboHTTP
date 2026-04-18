namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Represents a QPACK header field entry stored in the dynamic table.
/// </summary>
internal readonly record struct QpackEntry(string Name, string Value);