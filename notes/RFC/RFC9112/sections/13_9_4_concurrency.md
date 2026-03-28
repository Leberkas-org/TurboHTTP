---
title: "9.4.  Concurrency"
rfc_number: 9112
rfc_section: "9.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 9.4: Concurrency — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, concurrency]
---

## 9.4.  Concurrency

## 9.4  Concurrency

   A client ought to limit the number of simultaneous open connections
   that it maintains to a given server.

   Previous revisions of HTTP gave a specific number of connections as a
   ceiling, but this was found to be impractical for many applications.
   As a result, this specification does not mandate a particular maximum
   number of connections but, instead, encourages clients to be
   conservative when opening multiple connections.

   Multiple connections are typically used to avoid the "head-of-line
   blocking" problem, wherein a request that takes significant server-
   side processing and/or transfers very large content would block
   subsequent requests on the same connection.  However, each connection
   consumes server resources.

   Furthermore, using multiple connections can cause undesirable side
   effects in congested networks.  Using larger numbers of connections
   can also cause side effects in otherwise uncongested networks,
   because their aggregate and initially synchronized sending behavior
   can cause congestion that would not have been present if fewer
   parallel connections had been used.

   Note that a server might reject traffic that it deems abusive or
   characteristic of a denial-of-service attack, such as an excessive
   number of open connections from a single client.

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
