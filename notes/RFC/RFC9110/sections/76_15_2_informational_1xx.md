---
title: "15.2.  Informational 1xx"
rfc_number: 9110
rfc_section: "15.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 15.2: Informational 1xx — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, informational_1xx]
---

## 15.2.  Informational 1xx

## 15.2  Informational 1xx

   The 1xx (Informational) class of status code indicates an interim
   response for communicating connection status or request progress
   prior to completing the requested action and sending a final
   response.  Since HTTP/1.0 did not define any 1xx status codes, a
> **MUST NOT**: server MUST NOT send a 1xx response to an HTTP/1.0 client.

   A 1xx response is terminated by the end of the header section; it
   cannot contain content or trailers.

> **MUST**: A client MUST be able to parse one or more 1xx responses received
   prior to a final response, even if the client does not expect one.  A
> **MAY**: user agent MAY ignore unexpected 1xx responses.

> **MUST**: A proxy MUST forward 1xx responses unless the proxy itself requested
   the generation of the 1xx response.  For example, if a proxy adds an
   "Expect: 100-continue" header field when it forwards a request, then
   it need not forward the corresponding 100 (Continue) response(s).

15.2.1.  100 Continue

   The 100 (Continue) status code indicates that the initial part of a
   request has been received and has not yet been rejected by the
   server.  The server intends to send a final response after the
   request has been fully received and acted upon.

   When the request contains an Expect header field that includes a
   100-continue expectation, the 100 response indicates that the server
   wishes to receive the request content, as described in
   Section 10.1.1.  The client ought to continue sending the request and
   discard the 100 response.

   If the request did not contain an Expect header field containing the
   100-continue expectation, the client can simply discard this interim
   response.

15.2.2.  101 Switching Protocols

   The 101 (Switching Protocols) status code indicates that the server
   understands and is willing to comply with the client's request, via
   the Upgrade header field (Section 7.8), for a change in the
> **MUST**: application protocol being used on this connection.  The server MUST
   generate an Upgrade header field in the response that indicates which
   protocol(s) will be in effect after this response.

   It is assumed that the server will only agree to switch protocols
   when it is advantageous to do so.  For example, switching to a newer
   version of HTTP might be advantageous over older versions, and
   switching to a real-time, synchronous protocol might be advantageous
   when delivering resources that use such features.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
