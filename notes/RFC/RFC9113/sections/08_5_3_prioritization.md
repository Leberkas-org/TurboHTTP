---
title: "5.3.  Prioritization"
rfc_number: 9113
rfc_section: "5.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 5.3: Prioritization — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, prioritization]
---

## 5.3.  Prioritization

## 5.3  Prioritization

   In a multiplexed protocol like HTTP/2, prioritizing allocation of
   bandwidth and computation resources to streams can be critical to
   attaining good performance.  A poor prioritization scheme can result
   in HTTP/2 providing poor performance.  With no parallelism at the TCP
   layer, performance could be significantly worse than HTTP/1.1.

   A good prioritization scheme benefits from the application of
   contextual knowledge such as the content of resources, how resources
   are interrelated, and how those resources will be used by a peer.  In
   particular, clients can possess knowledge about the priority of
   requests that is relevant to server prioritization.  In those cases,
   having clients provide priority information can improve performance.

### 5.3.1  Background on Priority in RFC 7540

   RFC 7540 defined a rich system for signaling priority of requests.
   However, this system proved to be complex, and it was not uniformly
   implemented.

   The flexible scheme meant that it was possible for clients to express
   priorities in very different ways, with little consistency in the
   approaches that were adopted.  For servers, implementing generic
   support for the scheme was complex.  Implementation of priorities was
   uneven in both clients and servers.  Many server deployments ignored
   client signals when prioritizing their handling of requests.

   In short, the prioritization signaling in RFC 7540 [RFC7540] was not
   successful.

### 5.3.2  Priority Signaling in This Document

   This update to HTTP/2 deprecates the priority signaling defined in
   RFC 7540 [RFC7540].  The bulk of the text related to priority signals
   is not included in this document.  The description of frame fields
   and some of the mandatory handling is retained to ensure that
   implementations of this document remain interoperable with
   implementations that use the priority signaling described in RFC
   7540.

   A thorough description of the RFC 7540 priority scheme remains in
   Section 5.3 of [RFC7540].

   Signaling priority information is necessary to attain good
   performance in many cases.  Where signaling priority information is
   important, endpoints are encouraged to use an alternative scheme,
   such as the scheme described in [HTTP-PRIORITY].

   Though the priority signaling from RFC 7540 was not widely adopted,
   the information it provides can still be useful in the absence of
   better information.  Endpoints that receive priority signals in
   HEADERS or PRIORITY frames can benefit from applying that
   information.  In particular, implementations that consume these
   signals would not benefit from discarding these priority signals in
   the absence of alternatives.

> **SHOULD**: Servers SHOULD use other contextual information in determining
   priority of requests in the absence of any priority signals.  Servers
> **MAY**: MAY interpret the complete absence of signals as an indication that
   the client has not implemented the feature.  The defaults described
   in Section 5.3.5 of [RFC7540] are known to have poor performance
   under most conditions, and their use is unlikely to be deliberate.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
