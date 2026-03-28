---
title: "3.  Header Block Decoding"
rfc_number: 7541
rfc_section: "3"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Section 3: Header Block Decoding — RFC 7541 — HPACK: Header Compression for HTTP/2"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, header_block_decoding]
---

# 3.  Header Block Decoding


## 3.1.  Header Block Processing

   A decoder processes a header block sequentially to reconstruct the
   original header list.

   A header block is the concatenation of header field representations.
   The different possible header field representations are described in
   Section 6.



   Once a header field is decoded and added to the reconstructed header
   list, the header field cannot be removed.  A header field added to
   the header list can be safely passed to the application.

   By passing the resulting header fields to the application, a decoder
   can be implemented with minimal transitory memory commitment in
   addition to the memory required for the dynamic table.

## 3.2.  Header Field Representation Processing

   The processing of a header block to obtain a header list is defined
   in this section.  To ensure that the decoding will successfully
> **MUST**: produce a header list, a decoder MUST obey the following rules.

   All the header field representations contained in a header block are
   processed in the order in which they appear, as specified below.
   Details on the formatting of the various header field representations
   and some additional processing instructions are found in Section 6.

   An _indexed representation_ entails the following actions:

   o  The header field corresponding to the referenced entry in either
      the static table or dynamic table is appended to the decoded
      header list.

   A _literal representation_ that is _not added_ to the dynamic table
   entails the following action:

   o  The header field is appended to the decoded header list.

   A _literal representation_ that is _added_ to the dynamic table
   entails the following actions:

   o  The header field is appended to the decoded header list.

   o  The header field is inserted at the beginning of the dynamic
      table.  This insertion could result in the eviction of previous
      entries in the dynamic table (see Section 4.4).

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
