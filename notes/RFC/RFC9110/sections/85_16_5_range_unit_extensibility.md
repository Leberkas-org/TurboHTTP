---
title: "16.5.  Range Unit Extensibility"
rfc_number: 9110
rfc_section: "16.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 16.5: Range Unit Extensibility — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, range_unit_extensibility]
---

## 16.5.  Range Unit Extensibility

## 16.5  Range Unit Extensibility

### 16.5.1  Range Unit Registry

   The "HTTP Range Unit Registry" defines the namespace for the range
   unit names and refers to their corresponding specifications.  It is
   maintained at <https://www.iana.org/assignments/http-parameters>.

> **MUST**: Registration of an HTTP Range Unit MUST include the following fields:

   *  Name

   *  Description

   *  Pointer to specification text

   Values to be added to this namespace require IETF Review (see
   [RFC8126], Section 4.8).

### 16.5.2  Considerations for New Range Units

   Other range units, such as format-specific boundaries like pages,
   sections, records, rows, or time, are potentially usable in HTTP for
   application-specific purposes, but are not commonly used in practice.
   Implementors of alternative range units ought to consider how they
   would work with content codings and general-purpose intermediaries.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
