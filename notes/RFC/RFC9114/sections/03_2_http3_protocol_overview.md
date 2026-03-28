---
title: "2.  HTTP/3 Protocol Overview"
rfc_number: 9114
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 2: HTTP/3 Protocol Overview — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, http3_protocol_overview]
---

## 2.  HTTP/3 Protocol Overview

2.  HTTP/3 Protocol Overview

   HTTP/3 provides a transport for HTTP semantics using the QUIC
   transport protocol and an internal framing layer similar to HTTP/2.

   Once a client knows that an HTTP/3 server exists at a certain
   endpoint, it opens a QUIC connection.  QUIC provides protocol
   negotiation, stream-based multiplexing, and flow control.  Discovery
   of an HTTP/3 endpoint is described in Section 3.1.

   Within each stream, the basic unit of HTTP/3 communication is a frame
   (Section 7.2).  Each frame type serves a different purpose.  For
   example, HEADERS and DATA frames form the basis of HTTP requests and
   responses (Section 4.1).  Frames that apply to the entire connection
   are conveyed on a dedicated control stream.

   Multiplexing of requests is performed using the QUIC stream
   abstraction, which is described in Section 2 of [QUIC-TRANSPORT].
   Each request-response pair consumes a single QUIC stream.  Streams
   are independent of each other, so one stream that is blocked or
   suffers packet loss does not prevent progress on other streams.

   Server push is an interaction mode introduced in HTTP/2 ([HTTP/2])
   that permits a server to push a request-response exchange to a client
   in anticipation of the client making the indicated request.  This
   trades off network usage against a potential latency gain.  Several
   HTTP/3 frames are used to manage server push, such as PUSH_PROMISE,
   MAX_PUSH_ID, and CANCEL_PUSH.

   As in HTTP/2, request and response fields are compressed for
   transmission.  Because HPACK ([HPACK]) relies on in-order
   transmission of compressed field sections (a guarantee not provided
   by QUIC), HTTP/3 replaces HPACK with QPACK ([QPACK]).  QPACK uses
   separate unidirectional streams to modify and track field table
   state, while encoded field sections refer to the state of the table
   without modifying it.

## 2.1  Document Organization

   The following sections provide a detailed overview of the lifecycle
   of an HTTP/3 connection:

   *  "Connection Setup and Management" (Section 3) covers how an HTTP/3
      endpoint is discovered and an HTTP/3 connection is established.

   *  "Expressing HTTP Semantics in HTTP/3" (Section 4) describes how
      HTTP semantics are expressed using frames.

   *  "Connection Closure" (Section 5) describes how HTTP/3 connections
      are terminated, either gracefully or abruptly.

   The details of the wire protocol and interactions with the transport
   are described in subsequent sections:

   *  "Stream Mapping and Usage" (Section 6) describes the way QUIC
      streams are used.

   *  "HTTP Framing Layer" (Section 7) describes the frames used on most
      streams.

   *  "Error Handling" (Section 8) describes how error conditions are
      handled and expressed, either on a particular stream or for the
      connection as a whole.

   Additional resources are provided in the final sections:

   *  "Extensions to HTTP/3" (Section 9) describes how new capabilities
      can be added in future documents.

   *  A more detailed comparison between HTTP/2 and HTTP/3 can be found
      in Appendix A.

## 2.2  Conventions and Terminology

   The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and
   "OPTIONAL" in this document are to be interpreted as described in
   BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all
   capitals, as shown here.

   This document uses the variable-length integer encoding from
   [QUIC-TRANSPORT].

   The following terms are used:

   abort:  An abrupt termination of a connection or stream, possibly due
      to an error condition.

   client:  The endpoint that initiates an HTTP/3 connection.  Clients
      send HTTP requests and receive HTTP responses.

   connection:  A transport-layer connection between two endpoints using
      QUIC as the transport protocol.

   connection error:  An error that affects the entire HTTP/3
      connection.

   endpoint:  Either the client or server of the connection.

   frame:  The smallest unit of communication on a stream in HTTP/3,
      consisting of a header and a variable-length sequence of bytes
      structured according to the frame type.

      Protocol elements called "frames" exist in both this document and
      [QUIC-TRANSPORT].  Where frames from [QUIC-TRANSPORT] are
      referenced, the frame name will be prefaced with "QUIC".  For
      example, "QUIC CONNECTION_CLOSE frames".  References without this
      preface refer to frames defined in Section 7.2.

   HTTP/3 connection:  A QUIC connection where the negotiated
      application protocol is HTTP/3.

   peer:  An endpoint.  When discussing a particular endpoint, "peer"
      refers to the endpoint that is remote to the primary subject of
      discussion.

   receiver:  An endpoint that is receiving frames.

   sender:  An endpoint that is transmitting frames.

   server:  The endpoint that accepts an HTTP/3 connection.  Servers
      receive HTTP requests and send HTTP responses.

   stream:  A bidirectional or unidirectional bytestream provided by the
      QUIC transport.  All streams within an HTTP/3 connection can be
      considered "HTTP/3 streams", but multiple stream types are defined
      within HTTP/3.

   stream error:  An application-level error on the individual stream.

   The term "content" is defined in Section 6.4 of [HTTP].

   Finally, the terms "resource", "message", "user agent", "origin
   server", "gateway", "intermediary", "proxy", and "tunnel" are defined
   in Section 3 of [HTTP].

   Packet diagrams in this document use the format defined in
   Section 1.3 of [QUIC-TRANSPORT] to illustrate the order and size of
   fields.

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
