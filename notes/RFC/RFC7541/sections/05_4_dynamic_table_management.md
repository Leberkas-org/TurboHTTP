---
title: "4.  Dynamic Table Management"
rfc_number: 7541
rfc_section: "4"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Section 4: Dynamic Table Management — RFC 7541 — HPACK: Header Compression for HTTP/2"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, dynamic_table_management]
---

# 4.  Dynamic Table Management


   To limit the memory requirements on the decoder side, the dynamic
   table is constrained in size.








## 4.1.  Calculating Table Size

   The size of the dynamic table is the sum of the size of its entries.

   The size of an entry is the sum of its name's length in octets (as
   defined in Section 5.2), its value's length in octets, and 32.

   The size of an entry is calculated using the length of its name and
   value without any Huffman encoding applied.

      Note: The additional 32 octets account for an estimated overhead
      associated with an entry.  For example, an entry structure using
      two 64-bit pointers to reference the name and the value of the
      entry and two 64-bit integers for counting the number of
      references to the name and value would have 32 octets of overhead.

## 4.2.  Maximum Table Size

   Protocols that use HPACK determine the maximum size that the encoder
   is permitted to use for the dynamic table.  In HTTP/2, this value is
   determined by the SETTINGS_HEADER_TABLE_SIZE setting (see
   Section 6.5.2 of [HTTP2]).

   An encoder can choose to use less capacity than this maximum size
> **MUST**: (see Section 6.3), but the chosen size MUST stay lower than or equal
   to the maximum set by the protocol.

   A change in the maximum size of the dynamic table is signaled via a
   dynamic table size update (see Section 6.3).  This dynamic table size
> **MUST**: update MUST occur at the beginning of the first header block
   following the change to the dynamic table size.  In HTTP/2, this
   follows a settings acknowledgment (see Section 6.5.3 of [HTTP2]).

   Multiple updates to the maximum table size can occur between the
   transmission of two header blocks.  In the case that this size is
   changed more than once in this interval, the smallest maximum table
> **MUST**: size that occurs in that interval MUST be signaled in a dynamic table
   size update.  The final maximum size is always signaled, resulting in
   at most two dynamic table size updates.  This ensures that the
   decoder is able to perform eviction based on reductions in dynamic
   table size (see Section 4.3).

   This mechanism can be used to completely clear entries from the
   dynamic table by setting a maximum size of 0, which can subsequently
   be restored.






## 4.3.  Entry Eviction When Dynamic Table Size Changes

   Whenever the maximum size for the dynamic table is reduced, entries
   are evicted from the end of the dynamic table until the size of the
   dynamic table is less than or equal to the maximum size.

## 4.4.  Entry Eviction When Adding New Entries

   Before a new entry is added to the dynamic table, entries are evicted
   from the end of the dynamic table until the size of the dynamic table
   is less than or equal to (maximum size - new entry size) or until the
   table is empty.

   If the size of the new entry is less than or equal to the maximum
   size, that entry is added to the table.  It is not an error to
   attempt to add an entry that is larger than the maximum size; an
   attempt to add an entry larger than the maximum size causes the table
   to be emptied of all existing entries and results in an empty table.

   A new entry can reference the name of an entry in the dynamic table
   that will be evicted when adding this new entry into the dynamic
   table.  Implementations are cautioned to avoid deleting the
   referenced name if the referenced entry is evicted from the dynamic
   table prior to inserting the new entry.

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
