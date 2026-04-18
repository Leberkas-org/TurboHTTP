namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Discriminator for decoder instruction types (RFC 9204 §4.4).
/// </summary>
internal enum DecoderInstructionType
{
    SectionAcknowledgment,
    StreamCancellation,
    InsertCountIncrement
}