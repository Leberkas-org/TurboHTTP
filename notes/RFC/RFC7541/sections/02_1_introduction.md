---
title: "1.  Introduction"
rfc_number: 7541
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Section 1: Introduction — RFC 7541 — HPACK: Header Compression for HTTP/2"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, introduction]
---

# 1.  Introduction


   In HTTP/1.1 (see [RFC7230]), header fields are not compressed.  As
   web pages have grown to require dozens to hundreds of requests, the
   redundant header fields in these requests unnecessarily consume
   bandwidth, measurably increasing latency.

   SPDY [SPDY] initially addressed this redundancy by compressing header
   fields using the DEFLATE [DEFLATE] format, which proved very
   effective at efficiently representing the redundant header fields.
   However, that approach exposed a security risk as demonstrated by the
   CRIME (Compression Ratio Info-leak Made Easy) attack (see [CRIME]).

   This specification defines HPACK, a new compressor that eliminates
   redundant header fields, limits vulnerability to known security
   attacks, and has a bounded memory requirement for use in constrained
   environments.  Potential security concerns for HPACK are described in
   Section 7.

   The HPACK format is intentionally simple and inflexible.  Both
   characteristics reduce the risk of interoperability or security
   issues due to implementation error.  No extensibility mechanisms are
   defined; changes to the format are only possible by defining a
   complete replacement.

## 1.1.  Overview

   The format defined in this specification treats a list of header
   fields as an ordered collection of name-value pairs that can include
   duplicate pairs.  Names and values are considered to be opaque
   sequences of octets, and the order of header fields is preserved
   after being compressed and decompressed.

   Encoding is informed by header field tables that map header fields to
   indexed values.  These header field tables can be incrementally
   updated as new header fields are encoded or decoded.

   In the encoded form, a header field is represented either literally
   or as a reference to a header field in one of the header field
   tables.  Therefore, a list of header fields can be encoded using a
   mixture of references and literal values.

   Literal values are either encoded directly or use a static Huffman
   code.

   The encoder is responsible for deciding which header fields to insert
   as new entries in the header field tables.  The decoder executes the
   modifications to the header field tables prescribed by the encoder,



   reconstructing the list of header fields in the process.  This
   enables decoders to remain simple and interoperate with a wide
   variety of encoders.

   Examples illustrating the use of these different mechanisms to
   represent header fields are available in Appendix C.

## 1.2.  Conventions

> **MUST**: The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this
   document are to be interpreted as described in RFC 2119 [RFC2119].

   All numeric values are in network byte order.  Values are unsigned
   unless otherwise indicated.  Literal values are provided in decimal
   or hexadecimal as appropriate.

## 1.3.  Terminology

   This specification uses the following terms:

   Header Field:  A name-value pair.  Both the name and value are
      treated as opaque sequences of octets.

   Dynamic Table:  The dynamic table (see Section 2.3.2) is a table that
      associates stored header fields with index values.  This table is
      dynamic and specific to an encoding or decoding context.

   Static Table:  The static table (see Section 2.3.1) is a table that
      statically associates header fields that occur frequently with
      index values.  This table is ordered, read-only, always
      accessible, and it may be shared amongst all encoding or decoding
      contexts.

   Header List:  A header list is an ordered collection of header fields
      that are encoded jointly and can contain duplicate header fields.
      A complete list of header fields contained in an HTTP/2 header
      block is a header list.

   Header Field Representation:  A header field can be represented in
      encoded form either as a literal or as an index (see Section 2.4).

   Header Block:  An ordered list of header field representations,
      which, when decoded, yields a complete header list.

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
