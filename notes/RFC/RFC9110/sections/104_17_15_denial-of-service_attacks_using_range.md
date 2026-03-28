---
title: "17.15.  Denial-of-Service Attacks Using Range"
rfc_number: 9110
rfc_section: "17.15"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.15: Denial-of-Service Attacks Using Range — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, denial-of-service_attacks_using_range]
---

## 17.15.  Denial-of-Service Attacks Using Range

## 17.15  Denial-of-Service Attacks Using Range

   Unconstrained multiple range requests are susceptible to denial-of-
   service attacks because the effort required to request many
   overlapping ranges of the same data is tiny compared to the time,
   memory, and bandwidth consumed by attempting to serve the requested
   data in many parts.  Servers ought to ignore, coalesce, or reject
   egregious range requests, such as requests for more than two
   overlapping ranges or for many small ranges in a single set,
   particularly when the ranges are requested out of order for no
   apparent reason.  Multipart range requests are not designed to
   support random access.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
