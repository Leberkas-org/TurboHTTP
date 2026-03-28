---
title: "3.3.  Connections, Clients, and Servers"
rfc_number: 9110
rfc_section: "3.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.3: Connections, Clients, and Servers — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, connections_clients_and_servers]
---

## 3.3.  Connections, Clients, and Servers

## 3.3  Connections, Clients, and Servers

   HTTP is a client/server protocol that operates over a reliable
   transport- or session-layer "connection".

   An HTTP "client" is a program that establishes a connection to a
   server for the purpose of sending one or more HTTP requests.  An HTTP
   "server" is a program that accepts connections in order to service
   HTTP requests by sending HTTP responses.

   The terms client and server refer only to the roles that these
   programs perform for a particular connection.  The same program might
   act as a client on some connections and a server on others.

   HTTP is defined as a stateless protocol, meaning that each request
   message's semantics can be understood in isolation, and that the
   relationship between connections and messages on them has no impact
   on the interpretation of those messages.  For example, a CONNECT
   request (Section 9.3.6) or a request with the Upgrade header field
   (Section 7.8) can occur at any time, not just in the first message on
   a connection.  Many implementations depend on HTTP's stateless design
   in order to reuse proxied connections or dynamically load balance
   requests across multiple servers.

> **MUST NOT**: As a result, a server MUST NOT assume that two requests on the same
   connection are from the same user agent unless the connection is
   secured and specific to that agent.  Some non-standard HTTP
   extensions (e.g., [RFC4559]) have been known to violate this
   requirement, resulting in security and interoperability problems.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
