namespace TurboHttp.Diagnostics;

/// <summary>
/// Trace categories corresponding to TurboHttp architectural layers.
/// Powers of 2 enable bitwise combination for filtering.
/// </summary>
[Flags]
public enum TurboTraceCategory : ushort
{
    None = 0,
    Connection = 1,
    Protocol = 2,
    Request = 4,
    Response = 8,
    Cache = 16,
    Redirect = 32,
    Retry = 64,
    Pool = 128,
    Transport = 256,
    Stream = 512,
    All = 1023,
}
