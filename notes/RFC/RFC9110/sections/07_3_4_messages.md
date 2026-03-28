---
title: "3.4.  Messages"
rfc_number: 9110
rfc_section: "3.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.4: Messages — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, messages]
---

## 3.4.  Messages

## 3.4  Messages

   HTTP is a stateless request/response protocol for exchanging
   "messages" across a connection.  The terms "sender" and "recipient"
   refer to any implementation that sends or receives a given message,
   respectively.

   A client sends requests to a server in the form of a "request"
   message with a method (Section 9) and request target (Section 7.1).
   The request might also contain header fields (Section 6.3) for
   request modifiers, client information, and representation metadata,
   content (Section 6.4) intended for processing in accordance with the
   method, and trailer fields (Section 6.5) to communicate information
   collected while sending the content.

   A server responds to a client's request by sending one or more
   "response" messages, each including a status code (Section 15).  The
   response might also contain header fields for server information,
   resource metadata, and representation metadata, content to be
   interpreted in accordance with the status code, and trailer fields to
   communicate information collected while sending the content.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
