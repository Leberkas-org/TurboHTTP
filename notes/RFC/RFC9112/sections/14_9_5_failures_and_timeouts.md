---
title: "9.5.  Failures and Timeouts"
rfc_number: 9112
rfc_section: "9.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 9.5: Failures and Timeouts — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, failures_and_timeouts]
---

## 9.5.  Failures and Timeouts

## 9.5  Failures and Timeouts

   Servers will usually have some timeout value beyond which they will
   no longer maintain an inactive connection.  Proxy servers might make
   this a higher value since it is likely that the client will be making
   more connections through the same proxy server.  The use of
   persistent connections places no requirements on the length (or
   existence) of this timeout for either the client or the server.

> **SHOULD**: A client or server that wishes to time out SHOULD issue a graceful
   close on the connection.  Implementations SHOULD constantly monitor
   open connections for a received closure signal and respond to it as
   appropriate, since prompt closure of both sides of a connection
   enables allocated system resources to be reclaimed.

> **MAY**: A client, server, or proxy MAY close the transport connection at any
   time.  For example, a client might have started to send a new request
   at the same time that the server has decided to close the "idle"
   connection.  From the server's point of view, the connection is being
   closed while it was idle, but from the client's point of view, a
   request is in progress.

> **SHOULD**: A server SHOULD sustain persistent connections, when possible, and
   allow the underlying transport's flow-control mechanisms to resolve
   temporary overloads rather than terminate connections with the
   expectation that clients will retry.  The latter technique can
   exacerbate network congestion or server load.

> **SHOULD**: A client sending a message body SHOULD monitor the network connection
   for an error response while it is transmitting the request.  If the
   client sees a response that indicates the server does not wish to
   receive the message body and is closing the connection, the client
> **SHOULD**: SHOULD immediately cease transmitting the body and close its side of
   the connection.

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
