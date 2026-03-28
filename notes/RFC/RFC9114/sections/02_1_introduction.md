---
title: "1.  Introduction"
rfc_number: 9114
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 1: Introduction — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, introduction]
---

## 1.  Introduction

1.  Introduction

   HTTP semantics ([HTTP]) are used for a broad range of services on the
   Internet.  These semantics have most commonly been used with HTTP/1.1
   and HTTP/2.  HTTP/1.1 has been used over a variety of transport and
   session layers, while HTTP/2 has been used primarily with TLS over
   TCP.  HTTP/3 supports the same semantics over a new transport
   protocol: QUIC.

## 1.1  Prior Versions of HTTP

   HTTP/1.1 ([HTTP/1.1]) uses whitespace-delimited text fields to convey
   HTTP messages.  While these exchanges are human readable, using
   whitespace for message formatting leads to parsing complexity and
   excessive tolerance of variant behavior.

   Because HTTP/1.1 does not include a multiplexing layer, multiple TCP
   connections are often used to service requests in parallel.  However,
   that has a negative impact on congestion control and network
   efficiency, since TCP does not share congestion control across
   multiple connections.

   HTTP/2 ([HTTP/2]) introduced a binary framing and multiplexing layer
   to improve latency without modifying the transport layer.  However,
   because the parallel nature of HTTP/2's multiplexing is not visible
   to TCP's loss recovery mechanisms, a lost or reordered packet causes
   all active transactions to experience a stall regardless of whether
   that transaction was directly impacted by the lost packet.

## 1.2  Delegation to QUIC

   The QUIC transport protocol incorporates stream multiplexing and per-
   stream flow control, similar to that provided by the HTTP/2 framing
   layer.  By providing reliability at the stream level and congestion
   control across the entire connection, QUIC has the capability to
   improve the performance of HTTP compared to a TCP mapping.  QUIC also
   incorporates TLS 1.3 ([TLS]) at the transport layer, offering
   comparable confidentiality and integrity to running TLS over TCP,
   with the improved connection setup latency of TCP Fast Open ([TFO]).

   This document defines HTTP/3: a mapping of HTTP semantics over the
   QUIC transport protocol, drawing heavily on the design of HTTP/2.
   HTTP/3 relies on QUIC to provide confidentiality and integrity
   protection of data; peer authentication; and reliable, in-order, per-
   stream delivery.  While delegating stream lifetime and flow-control
   issues to QUIC, a binary framing similar to the HTTP/2 framing is
   used on each stream.  Some HTTP/2 features are subsumed by QUIC,
   while other features are implemented atop QUIC.

   QUIC is described in [QUIC-TRANSPORT].  For a full description of
   HTTP/2, see [HTTP/2].

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
