---
title: "4.1.  Primitives"
rfc_number: 9204
rfc_section: "4.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 4.1: Primitives — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, primitives]
---

## 4.1.  Primitives

4.  Wire Format

## 4.1  Primitives

### 4.1.1  Prefixed Integers

   The prefixed integer from Section 5.1 of [RFC7541] is used heavily
   throughout this document.  The format from [RFC7541] is used
   unmodified.  Note, however, that QPACK uses some prefix sizes not
   actually used in HPACK.

> **MUST**: QPACK implementations MUST be able to decode integers up to and
   including 62 bits long.

### 4.1.2  String Literals

   The string literal defined by Section 5.2 of [RFC7541] is also used
   throughout.  This string format includes optional Huffman encoding.

   HPACK defines string literals to begin on a byte boundary.  They
   begin with a single bit flag, denoted as 'H' in this document
   (indicating whether the string is Huffman encoded), followed by the
   string length encoded as a 7-bit prefix integer, and finally the
   indicated number of bytes of data.  When Huffman encoding is enabled,
   the Huffman table from Appendix B of [RFC7541] is used without
   modification and the indicated length is the size of the string after
   encoding.

   This document expands the definition of string literals by permitting
   them to begin other than on a byte boundary.  An "N-bit prefix string
   literal" begins mid-byte, with the first (8-N) bits allocated to a
   previous field.  The string uses one bit for the Huffman flag,
   followed by the length of the encoded string as a (N-1)-bit prefix
   integer.  The prefix size, N, can have a value between 2 and 8,
   inclusive.  The remainder of the string literal is unmodified.

   A string literal without a prefix length noted is an 8-bit prefix
   string literal and follows the definitions in [RFC7541] without
   modification.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
