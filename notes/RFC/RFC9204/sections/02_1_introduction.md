---
title: "1.  Introduction"
rfc_number: 9204
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 1: Introduction — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, introduction]
---

## 1.  Introduction

1.  Introduction

   The QUIC transport protocol ([QUIC-TRANSPORT]) is designed to support
   HTTP semantics, and its design subsumes many of the features of
   HTTP/2 ([HTTP/2]).  HTTP/2 uses HPACK ([RFC7541]) for compression of
   the header and trailer sections.  If HPACK were used for HTTP/3
   ([HTTP/3]), it would induce head-of-line blocking for field sections
   due to built-in assumptions of a total ordering across frames on all
   streams.

   QPACK reuses core concepts from HPACK, but is redesigned to allow
   correctness in the presence of out-of-order delivery, with
   flexibility for implementations to balance between resilience against
   head-of-line blocking and optimal compression ratio.  The design
   goals are to closely approach the compression ratio of HPACK with
   substantially less head-of-line blocking under the same loss
   conditions.

## 1.1  Conventions and Definitions

   The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and
   "OPTIONAL" in this document are to be interpreted as described in
   BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all
   capitals, as shown here.

   The following terms are used in this document:

   HTTP fields:  Metadata sent as part of an HTTP message.  The term
      encompasses both header and trailer fields.  Colloquially, the
      term "headers" has often been used to refer to HTTP header fields
      and trailer fields; this document uses "fields" for generality.

   HTTP field line:  A name-value pair sent as part of an HTTP field
      section.  See Sections 6.3 and 6.5 of [HTTP].

   HTTP field value:  Data associated with a field name, composed from
      all field line values with that field name in that section,
      concatenated together with comma separators.

   Field section:  An ordered collection of HTTP field lines associated
      with an HTTP message.  A field section can contain multiple field
      lines with the same name.  It can also contain duplicate field
      lines.  An HTTP message can include both header and trailer
      sections.

   Representation:  An instruction that represents a field line,
      possibly by reference to the dynamic and static tables.

   Encoder:  An implementation that encodes field sections.

   Decoder:  An implementation that decodes encoded field sections.

   Absolute Index:  A unique index for each entry in the dynamic table.

   Base:  A reference point for relative and post-Base indices.
      Representations that reference dynamic table entries are relative
      to a Base.

   Insert Count:  The total number of entries inserted in the dynamic
      table.

   Note that QPACK is a name, not an abbreviation.

## 1.2  Notational Conventions

   Diagrams in this document use the format described in Section 3.1 of
   [RFC2360], with the following additional conventions:

   x (A)  Indicates that x is A bits long.

   x (A+)  Indicates that x uses the prefixed integer encoding defined
      in Section 4.1.1, beginning with an A-bit prefix.

   x ...  Indicates that x is variable length and extends to the end of
      the region.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
