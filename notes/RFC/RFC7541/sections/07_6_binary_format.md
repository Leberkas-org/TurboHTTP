---
title: "6.  Binary Format"
rfc_number: 7541
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Section 6: Binary Format — RFC 7541 — HPACK: Header Compression for HTTP/2"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, binary_format]
---

# 6.  Binary Format


   This section describes the detailed format of each of the different
   header field representations and the dynamic table size update
   instruction.

## 6.1.  Indexed Header Field Representation

   An indexed header field representation identifies an entry in either
   the static table or the dynamic table (see Section 2.3).

   An indexed header field representation causes a header field to be
   added to the decoded header list, as described in Section 3.2.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 1 |        Index (7+)         |
   +---+---------------------------+

                      Figure 5: Indexed Header Field






   An indexed header field starts with the '1' 1-bit pattern, followed
   by the index of the matching header field, represented as an integer
   with a 7-bit prefix (see Section 5.1).

> **MUST**: The index value of 0 is not used.  It MUST be treated as a decoding
   error if found in an indexed header field representation.

## 6.2.  Literal Header Field Representation

   A literal header field representation contains a literal header field
   value.  Header field names are provided either as a literal or by
   reference to an existing table entry, either from the static table or
   the dynamic table (see Section 2.3).

   This specification defines three forms of literal header field
   representations: with indexing, without indexing, and never indexed.

### 6.2.1.  Literal Header Field with Incremental Indexing

   A literal header field with incremental indexing representation
   results in appending a header field to the decoded header list and
   inserting it as a new entry into the dynamic table.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 1 |      Index (6+)       |
   +---+---+-----------------------+
   | H |     Value Length (7+)     |
   +---+---------------------------+
   | Value String (Length octets)  |
   +-------------------------------+

    Figure 6: Literal Header Field with Incremental Indexing -- Indexed
                                   Name

















     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 1 |           0           |
   +---+---+-----------------------+
   | H |     Name Length (7+)      |
   +---+---------------------------+
   |  Name String (Length octets)  |
   +---+---------------------------+
   | H |     Value Length (7+)     |
   +---+---------------------------+
   | Value String (Length octets)  |
   +-------------------------------+

   Figure 7: Literal Header Field with Incremental Indexing -- New Name

   A literal header field with incremental indexing representation
   starts with the '01' 2-bit pattern.

   If the header field name matches the header field name of an entry
   stored in the static table or the dynamic table, the header field
   name can be represented using the index of that entry.  In this case,
   the index of the entry is represented as an integer with a 6-bit
   prefix (see Section 5.1).  This value is always non-zero.

   Otherwise, the header field name is represented as a string literal
   (see Section 5.2).  A value 0 is used in place of the 6-bit index,
   followed by the header field name.

   Either form of header field name representation is followed by the
   header field value represented as a string literal (see Section 5.2).

### 6.2.2.  Literal Header Field without Indexing

   A literal header field without indexing representation results in
   appending a header field to the decoded header list without altering
   the dynamic table.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 | 0 | 0 |  Index (4+)   |
   +---+---+-----------------------+
   | H |     Value Length (7+)     |
   +---+---------------------------+
   | Value String (Length octets)  |
   +-------------------------------+

      Figure 8: Literal Header Field without Indexing -- Indexed Name




     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 | 0 | 0 |       0       |
   +---+---+-----------------------+
   | H |     Name Length (7+)      |
   +---+---------------------------+
   |  Name String (Length octets)  |
   +---+---------------------------+
   | H |     Value Length (7+)     |
   +---+---------------------------+
   | Value String (Length octets)  |
   +-------------------------------+

        Figure 9: Literal Header Field without Indexing -- New Name

   A literal header field without indexing representation starts with
   the '0000' 4-bit pattern.

   If the header field name matches the header field name of an entry
   stored in the static table or the dynamic table, the header field
   name can be represented using the index of that entry.  In this case,
   the index of the entry is represented as an integer with a 4-bit
   prefix (see Section 5.1).  This value is always non-zero.

   Otherwise, the header field name is represented as a string literal
   (see Section 5.2).  A value 0 is used in place of the 4-bit index,
   followed by the header field name.

   Either form of header field name representation is followed by the
   header field value represented as a string literal (see Section 5.2).

### 6.2.3.  Literal Header Field Never Indexed

   A literal header field never-indexed representation results in
   appending a header field to the decoded header list without altering
> **MUST**: the dynamic table.  Intermediaries MUST use the same representation
   for encoding this header field.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 | 0 | 1 |  Index (4+)   |
   +---+---+-----------------------+
   | H |     Value Length (7+)     |
   +---+---------------------------+
   | Value String (Length octets)  |
   +-------------------------------+

       Figure 10: Literal Header Field Never Indexed -- Indexed Name



     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 | 0 | 1 |       0       |
   +---+---+-----------------------+
   | H |     Name Length (7+)      |
   +---+---------------------------+
   |  Name String (Length octets)  |
   +---+---------------------------+
   | H |     Value Length (7+)     |
   +---+---------------------------+
   | Value String (Length octets)  |
   +-------------------------------+

         Figure 11: Literal Header Field Never Indexed -- New Name

   A literal header field never-indexed representation starts with the
   '0001' 4-bit pattern.

   When a header field is represented as a literal header field never
> **MUST**: indexed, it MUST always be encoded with this specific literal
   representation.  In particular, when a peer sends a header field that
   it received represented as a literal header field never indexed, it
> **MUST**: MUST use the same representation to forward this header field.

   This representation is intended for protecting header field values
   that are not to be put at risk by compressing them (see Section 7.1
   for more details).

   The encoding of the representation is identical to the literal header
   field without indexing (see Section 6.2.2).

## 6.3.  Dynamic Table Size Update

   A dynamic table size update signals a change to the size of the
   dynamic table.

     0   1   2   3   4   5   6   7
   +---+---+---+---+---+---+---+---+
   | 0 | 0 | 1 |   Max size (5+)   |
   +---+---------------------------+

               Figure 12: Maximum Dynamic Table Size Change

   A dynamic table size update starts with the '001' 3-bit pattern,
   followed by the new maximum size, represented as an integer with a
   5-bit prefix (see Section 5.1).





> **MUST**: The new maximum size MUST be lower than or equal to the limit
   determined by the protocol using HPACK.  A value that exceeds this
> **MUST**: limit MUST be treated as a decoding error.  In HTTP/2, this limit is
   the last value of the SETTINGS_HEADER_TABLE_SIZE parameter (see
   Section 6.5.2 of [HTTP2]) received from the decoder and acknowledged
   by the encoder (see Section 6.5.3 of [HTTP2]).

   Reducing the maximum size of the dynamic table can cause entries to
   be evicted (see Section 4.3).

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
