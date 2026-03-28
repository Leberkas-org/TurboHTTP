---
title: "9.1.  Establishment"
rfc_number: 9112
rfc_section: "9.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 9.1: Establishment — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, establishment]
---

## 9.1.  Establishment

9.  Connection Management

   HTTP messaging is independent of the underlying transport- or
   session-layer connection protocol(s).  HTTP only presumes a reliable
   transport with in-order delivery of requests and the corresponding
   in-order delivery of responses.  The mapping of HTTP request and
   response structures onto the data units of an underlying transport
   protocol is outside the scope of this specification.

   As described in Section 7.3 of [HTTP], the specific connection
   protocols to be used for an HTTP interaction are determined by client
   configuration and the target URI.  For example, the "http" URI scheme
   (Section 4.2.1 of [HTTP]) indicates a default connection of TCP over
   IP, with a default TCP port of 80, but the client might be configured
   to use a proxy via some other connection, port, or protocol.

   HTTP implementations are expected to engage in connection management,
   which includes maintaining the state of current connections,
   establishing a new connection or reusing an existing connection,
   processing messages received on a connection, detecting connection
   failures, and closing each connection.  Most clients maintain
   multiple connections in parallel, including more than one connection
   per server endpoint.  Most servers are designed to maintain thousands
   of concurrent connections, while controlling request queues to enable
   fair use and detect denial-of-service attacks.

## 9.1  Establishment

   It is beyond the scope of this specification to describe how
   connections are established via various transport- or session-layer
   protocols.  Each HTTP connection maps to one underlying transport
   connection.

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
