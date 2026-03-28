---
title: "9.8.  TLS Connection Closure"
rfc_number: 9112
rfc_section: "9.8"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 9.8: TLS Connection Closure — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, tls_connection_closure]
---

## 9.8.  TLS Connection Closure

## 9.8  TLS Connection Closure

   TLS uses an exchange of closure alerts prior to (non-error)
   connection closure to provide secure connection closure; see
   Section 6.1 of [TLS13].  When a valid closure alert is received, an
   implementation can be assured that no further data will be received
   on that connection.

   When an implementation knows that it has sent or received all the
   message data that it cares about, typically by detecting HTTP message
   boundaries, it might generate an "incomplete close" by sending a
   closure alert and then closing the connection without waiting to
   receive the corresponding closure alert from its peer.

   An incomplete close does not call into question the security of the
   data already received, but it could indicate that subsequent data
   might have been truncated.  As TLS is not directly aware of HTTP
   message framing, it is necessary to examine the HTTP data itself to
   determine whether messages are complete.  Handling of incomplete
   messages is defined in Section 8.

> **SHOULD**: When encountering an incomplete close, a client SHOULD treat as
   completed all requests for which it has received either

   1.  as much data as specified in the Content-Length header field or

   2.  the terminal zero-length chunk (when Transfer-Encoding of chunked
       is used).

   A response that has neither chunked transfer coding nor Content-
   Length is complete only if a valid closure alert has been received.
   Treating an incomplete message as complete could expose
   implementations to attack.

> **SHOULD**: A client detecting an incomplete close SHOULD recover gracefully.

> **MUST**: Clients MUST send a closure alert before closing the connection.
   Clients that do not expect to receive any more data MAY choose not to
   wait for the server's closure alert and simply close the connection,
   thus generating an incomplete close on the server side.

> **SHOULD**: Servers SHOULD be prepared to receive an incomplete close from the
   client, since the client can often locate the end of server data.

> **MUST**: Servers MUST attempt to initiate an exchange of closure alerts with
   the client before closing the connection.  Servers MAY close the
   connection after sending the closure alert, thus generating an
   incomplete close on the client side.

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
