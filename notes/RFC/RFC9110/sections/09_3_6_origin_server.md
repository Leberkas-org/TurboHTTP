---
title: "3.6.  Origin Server"
rfc_number: 9110
rfc_section: "3.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.6: Origin Server — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, origin_server]
---

## 3.6.  Origin Server

## 3.6  Origin Server

   The term "origin server" refers to a program that can originate
   authoritative responses for a given target resource.

   The most familiar form of origin server are large public websites.
   However, like user agents being equated with browsers, it is easy to
   be misled into thinking that all origin servers are alike.  Common
   origin servers also include home automation units, configurable
   networking components, office machines, autonomous robots, news
   feeds, traffic cameras, real-time ad selectors, and video-on-demand
   platforms.

   Most HTTP communication consists of a retrieval request (GET) for a
   representation of some resource identified by a URI.  In the simplest
   case, this might be accomplished via a single bidirectional
   connection (===) between the user agent (UA) and the origin server
   (O).

            request   >
       UA ======================================= O
                                   <   response

                                  Figure 1

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
