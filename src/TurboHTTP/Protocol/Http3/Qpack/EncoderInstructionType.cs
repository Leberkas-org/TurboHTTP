namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Discriminator for encoder instruction types (RFC 9204 §4.3).
/// </summary>
internal enum EncoderInstructionType
{
    SetDynamicTableCapacity,
    InsertWithNameReference,
    InsertWithLiteralName,
    Duplicate
}