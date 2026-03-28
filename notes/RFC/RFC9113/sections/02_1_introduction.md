---
title: "1.  Introduction"
rfc_number: 9113
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 1: Introduction — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, introduction]
---

## 1.  Introduction

1.  Introduction

   The performance of applications using the Hypertext Transfer Protocol
   (HTTP, [HTTP]) is linked to how each version of HTTP uses the
   underlying transport, and the conditions under which the transport
   operates.

   Making multiple concurrent requests can reduce latency and improve
   application performance.  HTTP/1.0 allowed only one request to be
   outstanding at a time on a given TCP [TCP] connection.  HTTP/1.1
   [HTTP/1.1] added request pipelining, but this only partially
   addressed request concurrency and still suffers from application-
   layer head-of-line blocking.  Therefore, HTTP/1.0 and HTTP/1.1
   clients use multiple connections to a server to make concurrent
   requests.

   Furthermore, HTTP fields are often repetitive and verbose, causing
   unnecessary network traffic as well as causing the initial TCP
   congestion window to quickly fill.  This can result in excessive
   latency when multiple requests are made on a new TCP connection.

   HTTP/2 addresses these issues by defining an optimized mapping of
   HTTP's semantics to an underlying connection.  Specifically, it
   allows interleaving of messages on the same connection and uses an
   efficient coding for HTTP fields.  It also allows prioritization of
   requests, letting more important requests complete more quickly,
   further improving performance.

   The resulting protocol is more friendly to the network because fewer
   TCP connections can be used in comparison to HTTP/1.x.  This means
   less competition with other flows and longer-lived connections, which
   in turn lead to better utilization of available network capacity.
   Note, however, that TCP head-of-line blocking is not addressed by
   this protocol.

   Finally, HTTP/2 also enables more efficient processing of messages
   through use of binary message framing.

   This document obsoletes RFCs 7540 and 8740.  Appendix B lists notable
   changes.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
