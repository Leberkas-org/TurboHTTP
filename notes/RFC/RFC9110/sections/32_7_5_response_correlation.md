---
title: "7.5.  Response Correlation"
rfc_number: 9110
rfc_section: "7.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 7.5: Response Correlation — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, response_correlation]
---

## 7.5.  Response Correlation

## 7.5  Response Correlation

   A connection might be used for multiple request/response exchanges.
   The mechanism used to correlate between request and response messages
   is version dependent; some versions of HTTP use implicit ordering of
   messages, while others use an explicit identifier.

   All responses, regardless of the status code (including interim
   responses) can be sent at any time after a request is received, even
   if the request is not yet complete.  A response can complete before
   its corresponding request is complete (Section 6.1).  Likewise,
   clients are not expected to wait any specific amount of time for a
   response.  Clients (including intermediaries) might abandon a request
   if the response is not received within a reasonable period of time.

   A client that receives a response while it is still sending the
> **SHOULD**: associated request SHOULD continue sending that request unless it
   receives an explicit indication to the contrary (see, e.g.,
   Section 9.5 of [HTTP/1.1] and Section 6.4 of [HTTP/2]).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
