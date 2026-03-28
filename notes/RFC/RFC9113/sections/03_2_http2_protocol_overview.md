---
title: "2.  HTTP/2 Protocol Overview"
rfc_number: 9113
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 2: HTTP/2 Protocol Overview — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, http2_protocol_overview]
---

## 2.  HTTP/2 Protocol Overview

2.  HTTP/2 Protocol Overview

   HTTP/2 provides an optimized transport for HTTP semantics.  HTTP/2
   supports all of the core features of HTTP but aims to be more
   efficient than HTTP/1.1.

   HTTP/2 is a connection-oriented application-layer protocol that runs
   over a TCP connection ([TCP]).  The client is the TCP connection
   initiator.

   The basic protocol unit in HTTP/2 is a frame (Section 4.1).  Each
   frame type serves a different purpose.  For example, HEADERS and DATA
   frames form the basis of HTTP requests and responses (Section 8.1);
   other frame types like SETTINGS, WINDOW_UPDATE, and PUSH_PROMISE are
   used in support of other HTTP/2 features.

   Multiplexing of requests is achieved by having each HTTP request/
   response exchange associated with its own stream (Section 5).
   Streams are largely independent of each other, so a blocked or
   stalled request or response does not prevent progress on other
   streams.

   Effective use of multiplexing depends on flow control and
   prioritization.  Flow control (Section 5.2) ensures that it is
   possible to efficiently use multiplexed streams by restricting data
   that is transmitted to what the receiver is able to handle.
   Prioritization (Section 5.3) ensures that limited resources are used
   most effectively.  This revision of HTTP/2 deprecates the priority
   signaling scheme from [RFC7540].

   Because HTTP fields used in a connection can contain large amounts of
   redundant data, frames that contain them are compressed
   (Section 4.3).  This has especially advantageous impact upon request
   sizes in the common case, allowing many requests to be compressed
   into one packet.

   Finally, HTTP/2 adds a new, optional interaction mode whereby a
   server can push responses to a client (Section 8.4).  This is
   intended to allow a server to speculatively send data to a client
   that the server anticipates the client will need, trading off some
   network usage against a potential latency gain.  The server does this
   by synthesizing a request, which it sends as a PUSH_PROMISE frame.
   The server is then able to send a response to the synthetic request
   on a separate stream.

## 2.1  Document Organization

   The HTTP/2 specification is split into four parts:

   *  Starting HTTP/2 (Section 3) covers how an HTTP/2 connection is
      initiated.

   *  The frame (Section 4) and stream (Section 5) layers describe the
      way HTTP/2 frames are structured and formed into multiplexed
      streams.

   *  Frame (Section 6) and error (Section 7) definitions include
      details of the frame and error types used in HTTP/2.

   *  HTTP mappings (Section 8) and additional requirements (Section 9)
      describe how HTTP semantics are expressed using frames and
      streams.

   While some of the frame- and stream-layer concepts are isolated from
   HTTP, this specification does not define a completely generic frame
   layer.  The frame and stream layers are tailored to the needs of
   HTTP.

## 2.2  Conventions and Terminology

   The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and
   "OPTIONAL" in this document are to be interpreted as described in
   BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all
   capitals, as shown here.

   All numeric values are in network byte order.  Values are unsigned
   unless otherwise indicated.  Literal values are provided in decimal
   or hexadecimal as appropriate.  Hexadecimal literals are prefixed
   with "0x" to distinguish them from decimal literals.

   This specification describes binary formats using the conventions
   described in Section 1.3 of RFC 9000 [QUIC].  Note that this format
   uses network byte order and that high-valued bits are listed before
   low-valued bits.

   The following terms are used:

   client:  The endpoint that initiates an HTTP/2 connection.  Clients
      send HTTP requests and receive HTTP responses.

   connection:  A transport-layer connection between two endpoints.

   connection error:  An error that affects the entire HTTP/2
      connection.

   endpoint:  Either the client or server of the connection.

   frame:  The smallest unit of communication within an HTTP/2
      connection, consisting of a header and a variable-length sequence
      of octets structured according to the frame type.

   peer:  An endpoint.  When discussing a particular endpoint, "peer"
      refers to the endpoint that is remote to the primary subject of
      discussion.

   receiver:  An endpoint that is receiving frames.

   sender:  An endpoint that is transmitting frames.

   server:  The endpoint that accepts an HTTP/2 connection.  Servers
      receive HTTP requests and send HTTP responses.

   stream:  A bidirectional flow of frames within the HTTP/2 connection.

   stream error:  An error on the individual HTTP/2 stream.

   Finally, the terms "gateway", "intermediary", "proxy", and "tunnel"
   are defined in Section 3.7 of [HTTP].  Intermediaries act as both
   client and server at different times.

   The term "content" as it applies to message bodies is defined in
   Section 6.4 of [HTTP].

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
