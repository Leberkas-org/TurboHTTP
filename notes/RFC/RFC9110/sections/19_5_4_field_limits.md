---
title: "5.4.  Field Limits"
rfc_number: 9110
rfc_section: "5.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 5.4: Field Limits — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, field_limits]
---

## 5.4.  Field Limits

## 5.4  Field Limits

   HTTP does not place a predefined limit on the length of each field
   line, field value, or on the length of a header or trailer section as
   a whole, as described in Section 2.  Various ad hoc limitations on
   individual lengths are found in practice, often depending on the
   specific field's semantics.

   A server that receives a request header field line, field value, or
> **MUST**: set of fields larger than it wishes to process MUST respond with an
   appropriate 4xx (Client Error) status code.  Ignoring such header
   fields would increase the server's vulnerability to request smuggling
   attacks (Section 11.2 of [HTTP/1.1]).

> **MAY**: A client MAY discard or truncate received field lines that are larger
   than the client wishes to process if the field semantics are such
   that the dropped value(s) can be safely ignored without changing the
   message framing or response semantics.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
