namespace Servus.Akka.Diagnostics;

/// <summary>
/// Trace categories for the Servus.Akka transport layer.
/// Powers of 2 enable bitwise combination for filtering.
/// </summary>
[Flags]
public enum ServusTraceCategory : byte
{
    None = 0,
    Connection = 1,
    Dns = 2,
    Tls = 4,
    Pool = 8,
    All = 15,
}