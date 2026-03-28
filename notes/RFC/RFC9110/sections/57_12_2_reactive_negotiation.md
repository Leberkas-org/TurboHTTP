---
title: "12.2.  Reactive Negotiation"
rfc_number: 9110
rfc_section: "12.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 12.2: Reactive Negotiation — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, reactive_negotiation]
---

## 12.2.  Reactive Negotiation

## 12.2  Reactive Negotiation

   With "reactive negotiation" (a.k.a., "agent-driven negotiation"),
   selection of content (regardless of the status code) is performed by
   the user agent after receiving an initial response.  The mechanism
   for reactive negotiation might be as simple as a list of references
   to alternative representations.

   If the user agent is not satisfied by the initial response content,
   it can perform a GET request on one or more of the alternative
   resources to obtain a different representation.  Selection of such
   alternatives might be performed automatically (by the user agent) or
   manually (e.g., by the user selecting from a hypertext menu).

   A server might choose not to send an initial representation, other
   than the list of alternatives, and thereby indicate that reactive
   negotiation by the user agent is preferred.  For example, the
   alternatives listed in responses with the 300 (Multiple Choices) and
   406 (Not Acceptable) status codes include information about available
   representations so that the user or user agent can react by making a
   selection.

   Reactive negotiation is advantageous when the response would vary
   over commonly used dimensions (such as type, language, or encoding),
   when the origin server is unable to determine a user agent's
   capabilities from examining the request, and generally when public
   caches are used to distribute server load and reduce network usage.

   Reactive negotiation suffers from the disadvantages of transmitting a
   list of alternatives to the user agent, which degrades user-perceived
   latency if transmitted in the header section, and needing a second
   request to obtain an alternate representation.  Furthermore, this
   specification does not define a mechanism for supporting automatic
   selection, though it does not prevent such a mechanism from being
   developed.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
