---
title: "11.1.  Registration of HTTP/3 Identification String"
rfc_number: 9114
rfc_section: "11.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 11.1: Registration of HTTP/3 Identification String — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, registration_of_http3_identification_string]
---

## 11.1.  Registration of HTTP/3 Identification String

11.  IANA Considerations

   This document registers a new ALPN protocol ID (Section 11.1) and
   creates new registries that manage the assignment of code points in
   HTTP/3.

## 11.1  Registration of HTTP/3 Identification String

   This document creates a new registration for the identification of
   HTTP/3 in the "TLS Application-Layer Protocol Negotiation (ALPN)
   Protocol IDs" registry established in [RFC7301].

   The "h3" string identifies HTTP/3:

   Protocol:  HTTP/3

   Identification Sequence:  0x68 0x33 ("h3")

   Specification:  This document

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
