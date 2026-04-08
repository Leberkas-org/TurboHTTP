---
title: "6.  Stream Mapping and Usage"
rfc_number: 9114
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 6: Stream Mapping and Usage — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, stream_mapping_and_usage]
---

## 6.  Stream Mapping and Usage

6.  Stream Mapping and Usage

   A QUIC stream provides reliable in-order delivery of bytes, but makes
   no guarantees about order of delivery with regard to bytes on other
   streams.  In version 1 of QUIC, the stream data containing HTTP
   frames is carried by QUIC STREAM frames, but this framing is
   invisible to the HTTP framing layer.  The transport layer buffers and
   orders received stream data, exposing a reliable byte stream to the
   application.  Although QUIC permits out-of-order delivery within a
   stream, HTTP/3 does not make use of this feature.

   QUIC streams can be either unidirectional, carrying data only from
   initiator to receiver, or bidirectional, carrying data in both
   directions.  Streams can be initiated by either the client or the
   server.  For more detail on QUIC streams, see Section 2 of
   [QUIC-TRANSPORT].

   When HTTP fields and data are sent over QUIC, the QUIC layer handles
   most of the stream management.  HTTP does not need to do any separate
   multiplexing when using QUIC: data sent over a QUIC stream always
   maps to a particular HTTP transaction or to the entire HTTP/3
   connection context.

## 6.1  Bidirectional Streams

   All client-initiated bidirectional streams are used for HTTP requests
   and responses.  A bidirectional stream ensures that the response can
   be readily correlated with the request.  These streams are referred
   to as request streams.

   This means that the client's first request occurs on QUIC stream 0,
   with subsequent requests on streams 4, 8, and so on.  In order to
> **SHOULD**: permit these streams to open, an HTTP/3 server SHOULD configure non-
   zero minimum values for the number of permitted streams and the
   initial stream flow-control window.  So as to not unnecessarily limit
> **SHOULD**: parallelism, at least 100 request streams SHOULD be permitted at a
   time.

   HTTP/3 does not use server-initiated bidirectional streams, though an
> **MUST**: extension could define a use for these streams.  Clients MUST treat
   receipt of a server-initiated bidirectional stream as a connection
   error of type H3_STREAM_CREATION_ERROR unless such an extension has
   been negotiated.

## 6.2  Unidirectional Streams

   Unidirectional streams, in either direction, are used for a range of
   purposes.  The purpose is indicated by a stream type, which is sent
   as a variable-length integer at the start of the stream.  The format
   and structure of data that follows this integer is determined by the
   stream type.

   Unidirectional Stream Header {
     Stream Type (i),
   }

                   Figure 1: Unidirectional Stream Header

   Two stream types are defined in this document: control streams
   (Section 6.2.1) and push streams (Section 6.2.2).  [QPACK] defines
   two additional stream types.  Other stream types can be defined by
   extensions to HTTP/3; see Section 9 for more details.  Some stream
   types are reserved (Section 6.2.3).

   The performance of HTTP/3 connections in the early phase of their
   lifetime is sensitive to the creation and exchange of data on
   unidirectional streams.  Endpoints that excessively restrict the
   number of streams or the flow-control window of these streams will
   increase the chance that the remote peer reaches the limit early and
   becomes blocked.  In particular, implementations should consider that
   remote peers may wish to exercise reserved stream behavior
   (Section 6.2.3) with some of the unidirectional streams they are
   permitted to use.

   Each endpoint needs to create at least one unidirectional stream for
   the HTTP control stream.  QPACK requires two additional
   unidirectional streams, and other extensions might require further
   streams.  Therefore, the transport parameters sent by both clients
> **MUST**: and servers MUST allow the peer to create at least three
   unidirectional streams.  These transport parameters SHOULD also
   provide at least 1,024 bytes of flow-control credit to each
   unidirectional stream.

   Note that an endpoint is not required to grant additional credits to
   create more unidirectional streams if its peer consumes all the
   initial credits before creating the critical unidirectional streams.
> **SHOULD**: Endpoints SHOULD create the HTTP control stream as well as the
   unidirectional streams required by mandatory extensions (such as the
   QPACK encoder and decoder streams) first, and then create additional
   streams as allowed by their peer.

   If the stream header indicates a stream type that is not supported by
   the recipient, the remainder of the stream cannot be consumed as the
> **MUST**: semantics are unknown.  Recipients of unknown stream types MUST
   either abort reading of the stream or discard incoming data without
> **SHOULD**: further processing.  If reading is aborted, the recipient SHOULD use
   the H3_STREAM_CREATION_ERROR error code or a reserved error code
> **MUST NOT**: (Section 8.1).  The recipient MUST NOT consider unknown stream types
   to be a connection error of any kind.

   As certain stream types can affect connection state, a recipient
> **SHOULD NOT**: SHOULD NOT discard data from incoming unidirectional streams prior to
   reading the stream type.

> **MAY**: Implementations MAY send stream types before knowing whether the peer
   supports them.  However, stream types that could modify the state or
   semantics of existing protocol components, including QPACK or other
> **MUST NOT**: extensions, MUST NOT be sent until the peer is known to support them.

   A sender can close or reset a unidirectional stream unless otherwise
> **MUST**: specified.  A receiver MUST tolerate unidirectional streams being
   closed or reset prior to the reception of the unidirectional stream
   header.

### 6.2.1  Control Streams

   A control stream is indicated by a stream type of 0x00.  Data on this
   stream consists of HTTP/3 frames, as defined in Section 7.2.

> **MUST**: Each side MUST initiate a single control stream at the beginning of
   the connection and send its SETTINGS frame as the first frame on this
   stream.  If the first frame of the control stream is any other frame
> **MUST**: type, this MUST be treated as a connection error of type
   H3_MISSING_SETTINGS.  Only one control stream per peer is permitted;
> **MUST**: receipt of a second stream claiming to be a control stream MUST be
   treated as a connection error of type H3_STREAM_CREATION_ERROR.  The
> **MUST NOT**: sender MUST NOT close the control stream, and the receiver MUST NOT
   request that the sender close the control stream.  If either control
> **MUST**: stream is closed at any point, this MUST be treated as a connection
   error of type H3_CLOSED_CRITICAL_STREAM.  Connection errors are
   described in Section 8.

   Because the contents of the control stream are used to manage the
> **SHOULD**: behavior of other streams, endpoints SHOULD provide enough flow-
   control credit to keep the peer's control stream from becoming
   blocked.

   A pair of unidirectional streams is used rather than a single
   bidirectional stream.  This allows either peer to send data as soon
   as it is able.  Depending on whether 0-RTT is available on the QUIC
   connection, either client or server might be able to send stream data
   first.

### 6.2.2  Push Streams

   Server push is an optional feature introduced in HTTP/2 that allows a
   server to initiate a response before a request has been made.  See
   Section 4.6 for more details.

   A push stream is indicated by a stream type of 0x01, followed by the
   push ID of the promise that it fulfills, encoded as a variable-length
   integer.  The remaining data on this stream consists of HTTP/3
   frames, as defined in Section 7.2, and fulfills a promised server
   push by zero or more interim HTTP responses followed by a single
   final HTTP response, as defined in Section 4.1.  Server push and push
   IDs are described in Section 4.6.

   Only servers can push; if a server receives a client-initiated push
> **MUST**: stream, this MUST be treated as a connection error of type
   H3_STREAM_CREATION_ERROR.

   Push Stream Header {
     Stream Type (i) = 0x01,
     Push ID (i),
   }

                        Figure 2: Push Stream Header

> **SHOULD NOT**: A client SHOULD NOT abort reading on a push stream prior to reading
   the push stream header, as this could lead to disagreement between
   client and server on which push IDs have already been consumed.

> **MUST**: Each push ID MUST only be used once in a push stream header.  If a
   client detects that a push stream header includes a push ID that was
> **MUST**: used in another push stream header, the client MUST treat this as a
   connection error of type H3_ID_ERROR.

### 6.2.3  Reserved Stream Types

   Stream types of the format 0x1f * N + 0x21 for non-negative integer
   values of N are reserved to exercise the requirement that unknown
   types be ignored.  These streams have no semantics, and they can be
> **MAY**: sent when application-layer padding is desired.  They MAY also be
   sent on connections where no data is currently being transferred.
> **MUST NOT**: Endpoints MUST NOT consider these streams to have any meaning upon
   receipt.

   The payload and length of the stream are selected in any manner the
   sending implementation chooses.

---

## TurboHTTP Compliance

**Status**: ⚠️ Partial

### Implementation Notes

- **`Http3RequestStream.cs`** — Uses client-initiated bidirectional QUIC streams for request/response per §6.1; stream IDs follow QUIC numbering (0, 4, 8, …)
- **`Http3ControlStream.cs`** — Creates a single unidirectional control stream (type 0x00) at connection start per §6.2.1; sends SETTINGS as first frame; rejects duplicate control streams with `H3_STREAM_CREATION_ERROR`
- **`Http3StreamTypeDecoder.cs`** — Reads stream type from unidirectional stream headers; routes to appropriate handler or aborts unknown types with `H3_STREAM_CREATION_ERROR` per §6.2
- **`QpackEncoderStream.cs` / `QpackDecoderStream.cs`** — QPACK encoder and decoder unidirectional streams per §6.2 requirements

### Test References

- `TurboHTTP.Tests/RFC9114/04_Http3StreamTypeTests.cs` — Stream type identification and routing
- `TurboHTTP.Tests/RFC9114/05_Http3ControlStreamTests.cs` — Control stream lifecycle, SETTINGS-first validation
- `TurboHTTP.StreamTests/` — Stream multiplexing and bidirectional stream tests

### Known Gaps

- ❌ Push streams (§6.2.2) — not implemented; server-initiated push stream type (0x01) is rejected but push ID parsing is not validated
- ❌ Reserved stream types (§6.2.3) — not sent for connection padding; received reserved streams are correctly ignored
- ⚠️ Server-initiated bidirectional streams (§6.1) rejected with `H3_STREAM_CREATION_ERROR` as required, but error message could be more descriptive  When sending a reserved stream type,
> **MAY**: the implementation MAY either terminate the stream cleanly or reset
   it.  When resetting the stream, either the H3_NO_ERROR error code or
> **SHOULD**: a reserved error code (Section 8.1) SHOULD be used.

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
