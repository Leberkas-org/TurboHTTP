---
title: "4.4.  Decoder Instructions"
rfc_number: 9204
rfc_section: "4.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 4.4: Decoder Instructions — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, decoder_instructions]
---

## 4.4.  Decoder Instructions

## 4.4  Decoder Instructions

   A decoder sends decoder instructions on the decoder stream to inform
   the encoder about the processing of field sections and table updates
   to ensure consistency of the dynamic table.

### 4.4.1  Section Acknowledgment

   After processing an encoded field section whose declared Required
   Insert Count is not zero, the decoder emits a Section Acknowledgment
   instruction.  The instruction starts with the '1' 1-bit pattern,
   followed by the field section's associated stream ID encoded as a
   7-bit prefix integer; see Section 4.1.1.

   This instruction is used as described in Sections 2.1.4 and 2.2.2.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 1 |      Stream ID (7+)       |
   +---+---------------------------+

                      Figure 9: Section Acknowledgment

   If an encoder receives a Section Acknowledgment instruction referring
   to a stream on which every encoded field section with a non-zero
> **MUST**: Required Insert Count has already been acknowledged, this MUST be
   treated as a connection error of type QPACK_DECODER_STREAM_ERROR.

   The Section Acknowledgment instruction might increase the Known
   Received Count; see Section 2.1.4.

### 4.4.2  Stream Cancellation

   When a stream is reset or reading is abandoned, the decoder emits a
   Stream Cancellation instruction.  The instruction starts with the
   '01' 2-bit pattern, followed by the stream ID of the affected stream
   encoded as a 6-bit prefix integer.

   This instruction is used as described in Section 2.2.2.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 1 |     Stream ID (6+)    |
   +---+---+-----------------------+

                       Figure 10: Stream Cancellation

### 4.4.3  Insert Count Increment

   The Insert Count Increment instruction starts with the '00' 2-bit
   pattern, followed by the Increment encoded as a 6-bit prefix integer.
   This instruction increases the Known Received Count (Section 2.1.4)
   by the value of the Increment parameter.  The decoder should send an
   Increment value that increases the Known Received Count to the total
   number of dynamic table insertions and duplications processed so far.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 |     Increment (6+)    |
   +---+---+-----------------------+

                     Figure 11: Insert Count Increment

   An encoder that receives an Increment field equal to zero, or one
   that increases the Known Received Count beyond what the encoder has
> **MUST**: sent, MUST treat this as a connection error of type
   QPACK_DECODER_STREAM_ERROR.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
