---
title: "5.3.  Field Order"
rfc_number: 9110
rfc_section: "5.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 5.3: Field Order — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, field_order]
---

## 5.3.  Field Order

## 5.3  Field Order

> **MAY**: A recipient MAY combine multiple field lines within a field section
   that have the same field name into one field line, without changing
   the semantics of the message, by appending each subsequent field line
   value to the initial field line value in order, separated by a comma
   (",") and optional whitespace (OWS, defined in Section 5.6.3).  For
   consistency, use comma SP.

   The order in which field lines with the same name are received is
   therefore significant to the interpretation of the field value; a
> **MUST NOT**: proxy MUST NOT change the order of these field line values when
   forwarding a message.

   This means that, aside from the well-known exception noted below, a
> **MUST NOT**: sender MUST NOT generate multiple field lines with the same name in a
   message (whether in the headers or trailers) or append a field line
   when a field line of the same name already exists in the message,
   unless that field's definition allows multiple field line values to
   be recombined as a comma-separated list (i.e., at least one
   alternative of the field's definition allows a comma-separated list,
   such as an ABNF rule of #(values) defined in Section 5.6.1).

      |  *Note:* In practice, the "Set-Cookie" header field ([COOKIE])
      |  often appears in a response message across multiple field lines
      |  and does not use the list syntax, violating the above
      |  requirements on multiple field lines with the same field name.
      |  Since it cannot be combined into a single field value,
      |  recipients ought to handle "Set-Cookie" as a special case while
      |  processing fields.  (See Appendix A.2.3 of [Kri2001] for
      |  details.)

   The order in which field lines with differing field names are
   received in a section is not significant.  However, it is good
   practice to send header fields that contain additional control data
   first, such as Host on requests and Date on responses, so that
   implementations can decide when not to handle a message as early as
   possible.

> **MUST NOT**: A server MUST NOT apply a request to the target resource until it
   receives the entire request header section, since later header field
   lines might include conditionals, authentication credentials, or
   deliberately misleading duplicate header fields that could impact
   request processing.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
