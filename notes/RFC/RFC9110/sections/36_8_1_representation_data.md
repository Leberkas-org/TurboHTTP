---
title: "8.1.  Representation Data"
rfc_number: 9110
rfc_section: "8.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 8.1: Representation Data — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, representation_data]
---

## 8.1.  Representation Data

8.  Representation Data and Metadata

## 8.1  Representation Data

   The representation data associated with an HTTP message is either
   provided as the content of the message or referred to by the message
   semantics and the target URI.  The representation data is in a format
   and encoding defined by the representation metadata header fields.

   The data type of the representation data is determined via the header
   fields Content-Type and Content-Encoding.  These define a two-layer,
   ordered encoding model:

     representation-data := Content-Encoding( Content-Type( data ) )

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
