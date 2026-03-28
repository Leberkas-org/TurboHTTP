---
title: "6.5.  Trailer Fields"
rfc_number: 9110
rfc_section: "6.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 6.5: Trailer Fields — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, trailer_fields]
---

## 6.5.  Trailer Fields

## 6.5  Trailer Fields

   Fields (Section 5) that are located within a "trailer section" are
   referred to as "trailer fields" (or just "trailers", colloquially).
   Trailer fields can be useful for supplying message integrity checks,
   digital signatures, delivery metrics, or post-processing status
   information.

   Trailer fields ought to be processed and stored separately from the
   fields in the header section to avoid contradicting message semantics
   known at the time the header section was complete.  The presence or
   absence of certain header fields might impact choices made for the
   routing or processing of the message as a whole before the trailers
   are received; those choices cannot be unmade by the later discovery
   of trailer fields.

### 6.5.1  Limitations on Use of Trailers

   A trailer section is only possible when supported by the version of
   HTTP in use and enabled by an explicit framing mechanism.  For
   example, the chunked transfer coding in HTTP/1.1 allows a trailer
   section to be sent after the content (Section 7.1.2 of [HTTP/1.1]).

   Many fields cannot be processed outside the header section because
   their evaluation is necessary prior to receiving the content, such as
   those that describe message framing, routing, authentication, request
> **MUST NOT**: modifiers, response controls, or content format.  A sender MUST NOT
   generate a trailer field unless the sender knows the corresponding
   header field name's definition permits the field to be sent in
   trailers.

   Trailer fields can be difficult to process by intermediaries that
   forward messages from one protocol version to another.  If the entire
   message can be buffered in transit, some intermediaries could merge
   trailer fields into the header section (as appropriate) before it is
   forwarded.  However, in most cases, the trailers are simply
> **MUST NOT**: discarded.  A recipient MUST NOT merge a trailer field into a header
   section unless the recipient understands the corresponding header
   field definition and that definition explicitly permits and defines
   how trailer field values can be safely merged.

   The presence of the keyword "trailers" in the TE header field
   (Section 10.1.4) of a request indicates that the client is willing to
   accept trailer fields, on behalf of itself and any downstream
   clients.  For requests from an intermediary, this implies that all
   downstream clients are willing to accept trailer fields in the
   forwarded response.  Note that the presence of "trailers" does not
   mean that the client(s) will process any particular trailer field in
   the response; only that the trailer section(s) will not be dropped by
   any of the clients.

   Because of the potential for trailer fields to be discarded in
> **SHOULD NOT**: transit, a server SHOULD NOT generate trailer fields that it believes
   are necessary for the user agent to receive.

### 6.5.2  Processing Trailer Fields

   The "Trailer" header field (Section 6.6.2) can be sent to indicate
   fields likely to be sent in the trailer section, which allows
   recipients to prepare for their receipt before processing the
   content.  For example, this could be useful if a field name indicates
   that a dynamic checksum should be calculated as the content is
   received and then immediately checked upon receipt of the trailer
   field value.

   Like header fields, trailer fields with the same name are processed
   in the order received; multiple trailer field lines with the same
   name have the equivalent semantics as appending the multiple values
   as a list of members.  Trailer fields that might be generated more
> **MUST**: than once during a message MUST be defined as a list-based field even
   if each member value is only processed once per field line received.

> **MAY**: At the end of a message, a recipient MAY treat the set of received
   trailer fields as a data structure of name/value pairs, similar to
   (but separate from) the header fields.  Additional processing
   expectations, if any, can be defined within the field specification
   for a field intended for use in trailers.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
