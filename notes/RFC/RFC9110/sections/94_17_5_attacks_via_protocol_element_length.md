---
title: "17.5.  Attacks via Protocol Element Length"
rfc_number: 9110
rfc_section: "17.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.5: Attacks via Protocol Element Length — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, attacks_via_protocol_element_length]
---

## 17.5.  Attacks via Protocol Element Length

## 17.5  Attacks via Protocol Element Length

   Because HTTP uses mostly textual, character-delimited fields, parsers
   are often vulnerable to attacks based on sending very long (or very
   slow) streams of data, particularly where an implementation is
   expecting a protocol element with no predefined length (Section 2.3).

   To promote interoperability, specific recommendations are made for
   minimum size limits on fields (Section 5.4).  These are minimum
   recommendations, chosen to be supportable even by implementations
   with limited resources; it is expected that most implementations will
   choose substantially higher limits.

   A server can reject a message that has a target URI that is too long
   (Section 15.5.15) or request content that is too large
   (Section 15.5.14).  Additional status codes related to capacity
   limits have been defined by extensions to HTTP [RFC6585].

   Recipients ought to carefully limit the extent to which they process
   other protocol elements, including (but not limited to) request
   methods, response status phrases, field names, numeric values, and
   chunk lengths.  Failure to limit such processing can result in
   arbitrary code execution due to buffer or arithmetic overflows, and
   increased vulnerability to denial-of-service attacks.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
