using System.Text;

namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Parsed encoder instruction (RFC 9204 §4.3).
/// </summary>
internal sealed class EncoderInstruction
{
    public EncoderInstructionType Type { get; init; }

    /// <summary>Set Dynamic Table Capacity value, or Duplicate index.</summary>
    public int IntValue { get; init; }

    /// <summary>Insert With Name Reference: name index.</summary>
    public int NameIndex { get; init; }

    /// <summary>Insert With Name Reference: true if static table.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Insert With Name Reference / Literal Name: header name bytes (UTF-8).</summary>
    public byte[] Name { get; init; } = [];

    /// <summary>Insert instructions: header value bytes (UTF-8).</summary>
    public byte[] Value { get; init; } = [];

    /// <summary>Helper: Name as string.</summary>
    public string NameString => Encoding.UTF8.GetString(Name);

    /// <summary>Helper: Value as string.</summary>
    public string ValueString => Encoding.UTF8.GetString(Value);
}