---
title: "7.1.  Determining the Target Resource"
rfc_number: 9110
rfc_section: "7.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 7.1: Determining the Target Resource — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, determining_the_target_resource]
---

## 7.1.  Determining the Target Resource

7.  Routing HTTP Messages

   HTTP request message routing is determined by each client based on
   the target resource, the client's proxy configuration, and
   establishment or reuse of an inbound connection.  The corresponding
   response routing follows the same connection chain back to the
   client.

## 7.1  Determining the Target Resource

   Although HTTP is used in a wide variety of applications, most clients
   rely on the same resource identification mechanism and configuration
   techniques as general-purpose Web browsers.  Even when communication
   options are hard-coded in a client's configuration, we can think of
   their combined effect as a URI reference (Section 4.1).

   A URI reference is resolved to its absolute form in order to obtain
   the "target URI".  The target URI excludes the reference's fragment
   component, if any, since fragment identifiers are reserved for
   client-side processing ([URI], Section 3.5).

   To perform an action on a "target resource", the client sends a
   request message containing enough components of its parsed target URI
   to enable recipients to identify that same resource.  For historical
   reasons, the parsed target URI components, collectively referred to
   as the "request target", are sent within the message control data and
   the Host header field (Section 7.2).

   There are two unusual cases for which the request target components
   are in a method-specific form:

   *  For CONNECT (Section 9.3.6), the request target is the host name
      and port number of the tunnel destination, separated by a colon.

   *  For OPTIONS (Section 9.3.7), the request target can be a single
      asterisk ("*").

> **MUST**: See the respective method definitions for details.  These forms MUST
   NOT be used with other methods.

   Upon receipt of a client's request, a server reconstructs the target
   URI from the received components in accordance with their local
   configuration and incoming connection context.  This reconstruction
   is specific to each major protocol version.  For example, Section 3.3
   of [HTTP/1.1] defines how a server determines the target URI of an
   HTTP/1.1 request.

      |  *Note:* Previous specifications defined the recomposed target
      |  URI as a distinct concept, the "effective request URI".

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
