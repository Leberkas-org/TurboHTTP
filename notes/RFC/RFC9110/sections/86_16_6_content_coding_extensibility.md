---
title: "16.6.  Content Coding Extensibility"
rfc_number: 9110
rfc_section: "16.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 16.6: Content Coding Extensibility — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, content_coding_extensibility]
---

## 16.6.  Content Coding Extensibility

## 16.6  Content Coding Extensibility

### 16.6.1  Content Coding Registry

   The "HTTP Content Coding Registry", maintained by IANA at
   <https://www.iana.org/assignments/http-parameters/>, registers
   content-coding names.

> **MUST**: Content coding registrations MUST include the following fields:

   *  Name

   *  Description

   *  Pointer to specification text

> **MUST NOT**: Names of content codings MUST NOT overlap with names of transfer
   codings (per the "HTTP Transfer Coding Registry" located at
   <https://www.iana.org/assignments/http-parameters/>) unless the
   encoding transformation is identical (as is the case for the
   compression codings defined in Section 8.4.1).

   Values to be added to this namespace require IETF Review (see
> **MUST**: Section 4.8 of [RFC8126]) and MUST conform to the purpose of content
   coding defined in Section 8.4.1.

### 16.6.2  Considerations for New Content Codings

   New content codings ought to be self-descriptive whenever possible,
   with optional parameters discoverable within the coding format
   itself, rather than rely on external metadata that might be lost
   during transit.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
