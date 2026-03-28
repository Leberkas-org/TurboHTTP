---
title: "12.3.  Request Content Negotiation"
rfc_number: 9110
rfc_section: "12.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 12.3: Request Content Negotiation — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, request_content_negotiation]
---

## 12.3.  Request Content Negotiation

## 12.3  Request Content Negotiation

   When content negotiation preferences are sent in a server's response,
   the listed preferences are called "request content negotiation"
   because they intend to influence selection of an appropriate content
   for subsequent requests to that resource.  For example, the Accept
   (Section 12.5.1) and Accept-Encoding (Section 12.5.3) header fields
   can be sent in a response to indicate preferred media types and
   content codings for subsequent requests to that resource.

   Similarly, Section 3.1 of [RFC5789] defines the "Accept-Patch"
   response header field, which allows discovery of which content types
   are accepted in PATCH requests.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
