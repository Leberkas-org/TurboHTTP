---
title: "3.2.  Representations"
rfc_number: 9110
rfc_section: "3.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.2: Representations — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, representations]
---

## 3.2.  Representations

## 3.2  Representations

   A "representation" is information that is intended to reflect a past,
   current, or desired state of a given resource, in a format that can
   be readily communicated via the protocol.  A representation consists
   of a set of representation metadata and a potentially unbounded
   stream of representation data (Section 8).

   HTTP allows "information hiding" behind its uniform interface by
   defining communication with respect to a transferable representation
   of the resource state, rather than transferring the resource itself.
   This allows the resource identified by a URI to be anything,
   including temporal functions like "the current weather in Laguna
   Beach", while potentially providing information that represents that
   resource at the time a message is generated [REST].

   The uniform interface is similar to a window through which one can
   observe and act upon a thing only through the communication of
   messages to an independent actor on the other side.  A shared
   abstraction is needed to represent ("take the place of") the current
   or desired state of that thing in our communications.  When a
   representation is hypertext, it can provide both a representation of
   the resource state and processing instructions that help guide the
   recipient's future interactions.

   A target resource might be provided with, or be capable of
   generating, multiple representations that are each intended to
   reflect the resource's current state.  An algorithm, usually based on
   content negotiation (Section 12), would be used to select one of
   those representations as being most applicable to a given request.
   This "selected representation" provides the data and metadata for
   evaluating conditional requests (Section 13) and constructing the
   content for 200 (OK), 206 (Partial Content), and 304 (Not Modified)
   responses to GET (Section 9.3.1).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
