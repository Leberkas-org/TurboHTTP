namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Parsed decoder instruction (RFC 9204 §4.4).
/// </summary>
internal sealed class DecoderInstruction
{
    public DecoderInstructionType Type { get; init; }

    /// <summary>Stream ID (for Section Acknowledgment and Stream Cancellation) or increment value.</summary>
    public int IntValue { get; init; }
}