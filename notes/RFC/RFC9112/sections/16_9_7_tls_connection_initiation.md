---
title: "9.7.  TLS Connection Initiation"
rfc_number: 9112
rfc_section: "9.7"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 9.7: TLS Connection Initiation — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, tls_connection_initiation]
---

## 9.7.  TLS Connection Initiation

## 9.7  TLS Connection Initiation

   Conceptually, HTTP/TLS is simply sending HTTP messages over a
   connection secured via TLS [TLS13].

   The HTTP client also acts as the TLS client.  It initiates a
   connection to the server on the appropriate port and sends the TLS
   ClientHello to begin the TLS handshake.  When the TLS handshake has
   finished, the client may then initiate the first HTTP request.  All
> **MUST**: HTTP data MUST be sent as TLS "application data" but is otherwise
   treated like a normal connection for HTTP (including potential reuse
   as a persistent connection).

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
