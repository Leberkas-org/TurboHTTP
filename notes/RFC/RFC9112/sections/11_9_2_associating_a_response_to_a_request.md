---
title: "9.2.  Associating a Response to a Request"
rfc_number: 9112
rfc_section: "9.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 9.2: Associating a Response to a Request — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, associating_a_response_to_a_request]
---

## 9.2.  Associating a Response to a Request

## 9.2  Associating a Response to a Request

   HTTP/1.1 does not include a request identifier for associating a
   given request message with its corresponding one or more response
   messages.  Hence, it relies on the order of response arrival to
   correspond exactly to the order in which requests are made on the
   same connection.  More than one response message per request only
   occurs when one or more informational responses (1xx; see
   Section 15.2 of [HTTP]) precede a final response to the same request.

   A client that has more than one outstanding request on a connection
> **MUST**: MUST maintain a list of outstanding requests in the order sent and
   MUST associate each received response message on that connection to
   the first outstanding request that has not yet received a final (non-
   1xx) response.

   If a client receives data on a connection that doesn't have
> **MUST NOT**: outstanding requests, the client MUST NOT consider that data to be a
   valid response; the client SHOULD close the connection, since message
   delimitation is now ambiguous, unless the data consists only of one
   or more CRLF (which can be discarded per Section 2.2).

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
