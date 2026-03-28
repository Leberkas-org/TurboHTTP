---
title: "4.5.  Field Line Representations"
rfc_number: 9204
rfc_section: "4.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 4.5: Field Line Representations — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, field_line_representations]
---

## 4.5.  Field Line Representations

## 4.5  Field Line Representations

   An encoded field section consists of a prefix and a possibly empty
   sequence of representations defined in this section.  Each
   representation corresponds to a single field line.  These
   representations reference the static table or the dynamic table in a
   particular state, but they do not modify that state.

   Encoded field sections are carried in frames on streams defined by
   the enclosing protocol.

### 4.5.1  Encoded Field Section Prefix

   Each encoded field section is prefixed with two integers.  The
   Required Insert Count is encoded as an integer with an 8-bit prefix
   using the encoding described in Section 4.5.1.1.  The Base is encoded
   as a Sign bit ('S') and a Delta Base value with a 7-bit prefix; see
   Section 4.5.1.2.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   |   Required Insert Count (8+)  |
   +---+---------------------------+
   | S |      Delta Base (7+)      |
   +---+---------------------------+
   |      Encoded Field Lines    ...
   +-------------------------------+

                      Figure 12: Encoded Field Section

#### 4.5.1.1  Required Insert Count

   Required Insert Count identifies the state of the dynamic table
   needed to process the encoded field section.  Blocking decoders use
   the Required Insert Count to determine when it is safe to process the
   rest of the field section.

   The encoder transforms the Required Insert Count as follows before
   encoding:

      if ReqInsertCount == 0:

```abnf
         EncInsertCount = 0
      else:
         EncInsertCount = (ReqInsertCount mod (2 * MaxEntries)) + 1
```


   Here MaxEntries is the maximum number of entries that the dynamic
   table can have.  The smallest entry has empty name and value strings
   and has the size of 32.  Hence, MaxEntries is calculated as:


```abnf
      MaxEntries = floor( MaxTableCapacity / 32 )
```


   MaxTableCapacity is the maximum capacity of the dynamic table as
   specified by the decoder; see Section 3.2.3.

   This encoding limits the length of the prefix on long-lived
   connections.

   The decoder can reconstruct the Required Insert Count using an
   algorithm such as the following.  If the decoder encounters a value
   of EncodedInsertCount that could not have been produced by a
> **MUST**: conformant encoder, it MUST treat this as a connection error of type
   QPACK_DECOMPRESSION_FAILED.

   TotalNumberOfInserts is the total number of inserts into the
   decoder's dynamic table.


```abnf
      FullRange = 2 * MaxEntries
      if EncodedInsertCount == 0:
         ReqInsertCount = 0
      else:
         if EncodedInsertCount > FullRange:
            Error
         MaxValue = TotalNumberOfInserts + MaxEntries
```


         # MaxWrapped is the largest possible value of
         # ReqInsertCount that is 0 mod 2 * MaxEntries

```abnf
         MaxWrapped = floor(MaxValue / FullRange) * FullRange
         ReqInsertCount = MaxWrapped + EncodedInsertCount - 1
```


         # If ReqInsertCount exceeds MaxValue, the Encoder's value
         # must have wrapped one fewer time
         if ReqInsertCount > MaxValue:
            if ReqInsertCount <= FullRange:
               Error
            ReqInsertCount -= FullRange

         # Value of 0 must be encoded as 0.
         if ReqInsertCount == 0:
            Error

   For example, if the dynamic table is 100 bytes, then the Required
   Insert Count will be encoded modulo 6.  If a decoder has received 10
   inserts, then an encoded value of 4 indicates that the Required
   Insert Count is 9 for the field section.

#### 4.5.1.2  Base

   The Base is used to resolve references in the dynamic table as
   described in Section 3.2.5.

   To save space, the Base is encoded relative to the Required Insert
   Count using a one-bit Sign ('S' in Figure 12) and the Delta Base
   value.  A Sign bit of 0 indicates that the Base is greater than or
   equal to the value of the Required Insert Count; the decoder adds the
   value of Delta Base to the Required Insert Count to determine the
   value of the Base.  A Sign bit of 1 indicates that the Base is less
   than the Required Insert Count; the decoder subtracts the value of
   Delta Base from the Required Insert Count and also subtracts one to
   determine the value of the Base.  That is:

      if Sign == 0:

```abnf
         Base = ReqInsertCount + DeltaBase
      else:
         Base = ReqInsertCount - DeltaBase - 1
```


   A single-pass encoder determines the Base before encoding a field
   section.  If the encoder inserted entries in the dynamic table while
   encoding the field section and is referencing them, Required Insert
   Count will be greater than the Base, so the encoded difference is
   negative and the Sign bit is set to 1.  If the field section was not
   encoded using representations that reference the most recent entry in
   the table and did not insert any new entries, the Base will be
   greater than the Required Insert Count, so the encoded difference
   will be positive and the Sign bit is set to 0.

> **MUST NOT**: The value of Base MUST NOT be negative.  Though the protocol might
   operate correctly with a negative Base using post-Base indexing, it
> **MUST**: is unnecessary and inefficient.  An endpoint MUST treat a field block
   with a Sign bit of 1 as invalid if the value of Required Insert Count
   is less than or equal to the value of Delta Base.

   An encoder that produces table updates before encoding a field
   section might set Base to the value of Required Insert Count.  In
   such a case, both the Sign bit and the Delta Base will be set to
   zero.

   A field section that was encoded without references to the dynamic
   table can use any value for the Base; setting Delta Base to zero is
   one of the most efficient encodings.

   For example, with a Required Insert Count of 9, a decoder receives a
   Sign bit of 1 and a Delta Base of 2.  This sets the Base to 6 and
   enables post-Base indexing for three entries.  In this example, a
   relative index of 1 refers to the fifth entry that was added to the
   table; a post-Base index of 1 refers to the eighth entry.

### 4.5.2  Indexed Field Line

   An indexed field line representation identifies an entry in the
   static table or an entry in the dynamic table with an absolute index
   less than the value of the Base.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 1 | T |      Index (6+)       |
   +---+---+-----------------------+

                       Figure 13: Indexed Field Line

   This representation starts with the '1' 1-bit pattern, followed by
   the 'T' bit, indicating whether the reference is into the static or
   dynamic table.  The 6-bit prefix integer (Section 4.1.1) that follows
   is used to locate the table entry for the field line.  When T=1, the
   number represents the static table index; when T=0, the number is the
   relative index of the entry in the dynamic table.

### 4.5.3  Indexed Field Line with Post-Base Index

   An indexed field line with post-Base index representation identifies
   an entry in the dynamic table with an absolute index greater than or
   equal to the value of the Base.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 | 0 | 1 |  Index (4+)   |
   +---+---+---+---+---------------+

             Figure 14: Indexed Field Line with Post-Base Index

   This representation starts with the '0001' 4-bit pattern.  This is
   followed by the post-Base index (Section 3.2.6) of the matching field
   line, represented as an integer with a 4-bit prefix; see
   Section 4.1.1.

### 4.5.4  Literal Field Line with Name Reference

   A literal field line with name reference representation encodes a
   field line where the field name matches the field name of an entry in
   the static table or the field name of an entry in the dynamic table
   with an absolute index less than the value of the Base.

        0   1   2   3   4   5   6   7
      +---+---+---+---+---+---+---+---+
      | 0 | 1 | N | T |Name Index (4+)|
      +---+---+---+---+---------------+
      | H |     Value Length (7+)     |
      +---+---------------------------+
      |  Value String (Length bytes)  |
      +-------------------------------+

             Figure 15: Literal Field Line with Name Reference

   This representation starts with the '01' 2-bit pattern.  The
   following bit, 'N', indicates whether an intermediary is permitted to
   add this field line to the dynamic table on subsequent hops.  When
> **MUST**: the 'N' bit is set, the encoded field line MUST always be encoded
   with a literal representation.  In particular, when a peer sends a
   field line that it received represented as a literal field line with
> **MUST**: the 'N' bit set, it MUST use a literal representation to forward this
   field line.  This bit is intended for protecting field values that
   are not to be put at risk by compressing them; see Section 7.1 for
   more details.

   The fourth ('T') bit indicates whether the reference is to the static
   or dynamic table.  The 4-bit prefix integer (Section 4.1.1) that
   follows is used to locate the table entry for the field name.  When
   T=1, the number represents the static table index; when T=0, the
   number is the relative index of the entry in the dynamic table.

   Only the field name is taken from the dynamic table entry; the field
   value is encoded as an 8-bit prefix string literal; see
   Section 4.1.2.

### 4.5.5  Literal Field Line with Post-Base Name Reference

   A literal field line with post-Base name reference representation
   encodes a field line where the field name matches the field name of a
   dynamic table entry with an absolute index greater than or equal to
   the value of the Base.

        0   1   2   3   4   5   6   7
      +---+---+---+---+---+---+---+---+
      | 0 | 0 | 0 | 0 | N |NameIdx(3+)|
      +---+---+---+---+---+-----------+
      | H |     Value Length (7+)     |
      +---+---------------------------+
      |  Value String (Length bytes)  |
      +-------------------------------+

        Figure 16: Literal Field Line with Post-Base Name Reference

   This representation starts with the '0000' 4-bit pattern.  The fifth
   bit is the 'N' bit as described in Section 4.5.4.  This is followed
   by a post-Base index of the dynamic table entry (Section 3.2.6)
   encoded as an integer with a 3-bit prefix; see Section 4.1.1.

   Only the field name is taken from the dynamic table entry; the field
   value is encoded as an 8-bit prefix string literal; see
   Section 4.1.2.

### 4.5.6  Literal Field Line with Literal Name

   The literal field line with literal name representation encodes a
   field name and a field value as string literals.

        0   1   2   3   4   5   6   7
      +---+---+---+---+---+---+---+---+
      | 0 | 0 | 1 | N | H |NameLen(3+)|
      +---+---+---+---+---+-----------+
      |  Name String (Length bytes)   |
      +---+---------------------------+
      | H |     Value Length (7+)     |
      +---+---------------------------+
      |  Value String (Length bytes)  |
      +-------------------------------+

              Figure 17: Literal Field Line with Literal Name

   This representation starts with the '001' 3-bit pattern.  The fourth
   bit is the 'N' bit as described in Section 4.5.4.  The name follows,
   represented as a 4-bit prefix string literal, then the value,
   represented as an 8-bit prefix string literal; see Section 4.1.2.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
