---
title: "2.  Compression Process Overview"
rfc_number: 7541
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Section 2: Compression Process Overview — RFC 7541 — HPACK: Header Compression for HTTP/2"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, compression_process_overview]
---

# 2.  Compression Process Overview


   This specification does not describe a specific algorithm for an
   encoder.  Instead, it defines precisely how a decoder is expected to
   operate, allowing encoders to produce any encoding that this
   definition permits.

## 2.1.  Header List Ordering

   HPACK preserves the ordering of header fields inside the header list.
> **MUST**: An encoder MUST order header field representations in the header
   block according to their ordering in the original header list.  A
> **MUST**: decoder MUST order header fields in the decoded header list according
   to their ordering in the header block.

## 2.2.  Encoding and Decoding Contexts

   To decompress header blocks, a decoder only needs to maintain a
   dynamic table (see Section 2.3.2) as a decoding context.  No other
   dynamic state is needed.

   When used for bidirectional communication, such as in HTTP, the
   encoding and decoding dynamic tables maintained by an endpoint are
   completely independent, i.e., the request and response dynamic tables
   are separate.

## 2.3.  Indexing Tables

   HPACK uses two tables for associating header fields to indexes.  The
   static table (see Section 2.3.1) is predefined and contains common
   header fields (most of them with an empty value).  The dynamic table
   (see Section 2.3.2) is dynamic and can be used by the encoder to
   index header fields repeated in the encoded header lists.

   These two tables are combined into a single address space for
   defining index values (see Section 2.3.3).

### 2.3.1.  Static Table

   The static table consists of a predefined static list of header
   fields.  Its entries are defined in Appendix A.

### 2.3.2.  Dynamic Table

   The dynamic table consists of a list of header fields maintained in
   first-in, first-out order.  The first and newest entry in a dynamic
   table is at the lowest index, and the oldest entry of a dynamic table
   is at the highest index.



   The dynamic table is initially empty.  Entries are added as each
   header block is decompressed.

   The dynamic table can contain duplicate entries (i.e., entries with
> **MUST NOT**: the same name and same value).  Therefore, duplicate entries MUST NOT
   be treated as an error by a decoder.

   The encoder decides how to update the dynamic table and as such can
   control how much memory is used by the dynamic table.  To limit the
   memory requirements of the decoder, the dynamic table size is
   strictly bounded (see Section 4.2).

   The decoder updates the dynamic table during the processing of a list
   of header field representations (see Section 3.2).

### 2.3.3.  Index Address Space

   The static table and the dynamic table are combined into a single
   index address space.

   Indices between 1 and the length of the static table (inclusive)
   refer to elements in the static table (see Section 2.3.1).

   Indices strictly greater than the length of the static table refer to
   elements in the dynamic table (see Section 2.3.2).  The length of the
   static table is subtracted to find the index into the dynamic table.

   Indices strictly greater than the sum of the lengths of both tables
> **MUST**: MUST be treated as a decoding error.

   For a static table size of s and a dynamic table size of k, the
   following diagram shows the entire valid index address space.

           <----------  Index Address Space ---------->
           <-- Static  Table -->  <-- Dynamic Table -->
           +---+-----------+---+  +---+-----------+---+
           | 1 |    ...    | s |  |s+1|    ...    |s+k|
           +---+-----------+---+  +---+-----------+---+
                                  ^                   |
                                  |                   V
                           Insertion Point      Dropping Point

                       Figure 1: Index Address Space








## 2.4.  Header Field Representation

   An encoded header field can be represented either as an index or as a
   literal.

   An indexed representation defines a header field as a reference to an
   entry in either the static table or the dynamic table (see
   Section 6.1).

   A literal representation defines a header field by specifying its
   name and value.  The header field name can be represented literally
   or as a reference to an entry in either the static table or the
   dynamic table.  The header field value is represented literally.

   Three different literal representations are defined:

   o  A literal representation that adds the header field as a new entry
      at the beginning of the dynamic table (see Section 6.2.1).

   o  A literal representation that does not add the header field to the
      dynamic table (see Section 6.2.2).

   o  A literal representation that does not add the header field to the
      dynamic table, with the additional stipulation that this header
      field always use a literal representation, in particular when re-
      encoded by an intermediary (see Section 6.2.3).  This
      representation is intended for protecting header field values that
      are not to be put at risk by compressing them (see Section 7.1.3
      for more details).

   The selection of one of these literal representations can be guided
   by security considerations, in order to protect sensitive header
   field values (see Section 7.1).

   The literal representation of a header field name or of a header
   field value can encode the sequence of octets either directly or
   using a static Huffman code (see Section 5.2).

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
