---
title: "4.3.  Encoder Instructions"
rfc_number: 9204
rfc_section: "4.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 4.3: Encoder Instructions — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, encoder_instructions]
---

## 4.3.  Encoder Instructions

## 4.3  Encoder Instructions

   An encoder sends encoder instructions on the encoder stream to set
   the capacity of the dynamic table and add dynamic table entries.
   Instructions adding table entries can use existing entries to avoid
   transmitting redundant information.  The name can be transmitted as a
   reference to an existing entry in the static or the dynamic table or
   as a string literal.  For entries that already exist in the dynamic
   table, the full entry can also be used by reference, creating a
   duplicate entry.

### 4.3.1  Set Dynamic Table Capacity

   An encoder informs the decoder of a change to the dynamic table
   capacity using an instruction that starts with the '001' 3-bit
   pattern.  This is followed by the new dynamic table capacity
   represented as an integer with a 5-bit prefix; see Section 4.1.1.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 | 1 |   Capacity (5+)   |
   +---+---+---+-------------------+

                    Figure 5: Set Dynamic Table Capacity

> **MUST**: The new capacity MUST be lower than or equal to the limit described
   in Section 3.2.3.  In HTTP/3, this limit is the value of the
   SETTINGS_QPACK_MAX_TABLE_CAPACITY parameter (Section 5) received from
> **MUST**: the decoder.  The decoder MUST treat a new dynamic table capacity
   value that exceeds this limit as a connection error of type
   QPACK_ENCODER_STREAM_ERROR.

   Reducing the dynamic table capacity can cause entries to be evicted;
> **MUST NOT**: see Section 3.2.2.  This MUST NOT cause the eviction of entries that
   are not evictable; see Section 2.1.1.  Changing the capacity of the
   dynamic table is not acknowledged as this instruction does not insert
   an entry.

### 4.3.2  Insert with Name Reference

   An encoder adds an entry to the dynamic table where the field name
   matches the field name of an entry stored in the static or the
   dynamic table using an instruction that starts with the '1' 1-bit
   pattern.  The second ('T') bit indicates whether the reference is to
   the static or dynamic table.  The 6-bit prefix integer
   (Section 4.1.1) that follows is used to locate the table entry for
   the field name.  When T=1, the number represents the static table
   index; when T=0, the number is the relative index of the entry in the
   dynamic table.

   The field name reference is followed by the field value represented
   as a string literal; see Section 4.1.2.

        0   1   2   3   4   5   6   7
      +---+---+---+---+---+---+---+---+
      | 1 | T |    Name Index (6+)    |
      +---+---+-----------------------+
      | H |     Value Length (7+)     |
      +---+---------------------------+
      |  Value String (Length bytes)  |
      +-------------------------------+

                Figure 6: Insert Field Line -- Indexed Name

### 4.3.3  Insert with Literal Name

   An encoder adds an entry to the dynamic table where both the field
   name and the field value are represented as string literals using an
   instruction that starts with the '01' 2-bit pattern.

   This is followed by the name represented as a 6-bit prefix string
   literal and the value represented as an 8-bit prefix string literal;
   see Section 4.1.2.

        0   1   2   3   4   5   6   7
      +---+---+---+---+---+---+---+---+
      | 0 | 1 | H | Name Length (5+)  |
      +---+---+---+-------------------+
      |  Name String (Length bytes)   |
      +---+---------------------------+
      | H |     Value Length (7+)     |
      +---+---------------------------+
      |  Value String (Length bytes)  |
      +-------------------------------+

                  Figure 7: Insert Field Line -- New Name

### 4.3.4  Duplicate

   An encoder duplicates an existing entry in the dynamic table using an
   instruction that starts with the '000' 3-bit pattern.  This is
   followed by the relative index of the existing entry represented as
   an integer with a 5-bit prefix; see Section 4.1.1.

        0   1   2   3   4   5   6   7
      +---+---+---+---+---+---+---+---+
      | 0 | 0 | 0 |    Index (5+)     |
      +---+---+---+-------------------+

                            Figure 8: Duplicate

   The existing entry is reinserted into the dynamic table without
   resending either the name or the value.  This is useful to avoid
   adding a reference to an older entry, which might block inserting new
   entries.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
