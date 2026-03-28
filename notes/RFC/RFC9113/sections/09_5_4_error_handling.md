---
title: "5.4.  Error Handling"
rfc_number: 9113
rfc_section: "5.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 5.4: Error Handling — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, error_handling]
---

## 5.4.  Error Handling

## 5.4  Error Handling

   HTTP/2 framing permits two classes of errors:

   *  An error condition that renders the entire connection unusable is
      a connection error.

   *  An error in an individual stream is a stream error.

   A list of error codes is included in Section 7.

   It is possible that an endpoint will encounter frames that would
> **MAY**: cause multiple errors.  Implementations MAY discover multiple errors
   during processing, but they SHOULD report at most one stream and one
   connection error as a result.

   The first stream error reported for a given stream prevents any other
   errors on that stream from being reported.  In comparison, the
> **SHOULD**: protocol permits multiple GOAWAY frames, though an endpoint SHOULD
   report just one type of connection error unless an error is
   encountered during graceful shutdown.  If this occurs, an endpoint
> **MAY**: MAY send an additional GOAWAY frame with the new error code, in
   addition to any prior GOAWAY that contained NO_ERROR.

> **MAY**: If an endpoint detects multiple different errors, it MAY choose to
   report any one of those errors.  If a frame causes a connection
> **MUST**: error, that error MUST be reported.  Additionally, an endpoint MAY
   use any applicable error code when it detects an error condition; a
   generic error code (such as PROTOCOL_ERROR or INTERNAL_ERROR) can
   always be used in place of more specific error codes.

### 5.4.1  Connection Error Handling

   A connection error is any error that prevents further processing of
   the frame layer or corrupts any connection state.

> **SHOULD**: An endpoint that encounters a connection error SHOULD first send a
   GOAWAY frame (Section 6.8) with the stream identifier of the last
   stream that it successfully received from its peer.  The GOAWAY frame
   includes an error code (Section 7) that indicates why the connection
   is terminating.  After sending the GOAWAY frame for an error
> **MUST**: condition, the endpoint MUST close the TCP connection.

   It is possible that the GOAWAY will not be reliably received by the
   receiving endpoint.  In the event of a connection error, GOAWAY only
   provides a best-effort attempt to communicate with the peer about why
   the connection is being terminated.

   An endpoint can end a connection at any time.  In particular, an
> **MAY**: endpoint MAY choose to treat a stream error as a connection error.
   Endpoints SHOULD send a GOAWAY frame when ending a connection,
   providing that circumstances permit it.

### 5.4.2  Stream Error Handling

   A stream error is an error related to a specific stream that does not
   affect processing of other streams.

   An endpoint that detects a stream error sends a RST_STREAM frame
   (Section 6.4) that contains the stream identifier of the stream where
   the error occurred.  The RST_STREAM frame includes an error code that
   indicates the type of error.

   A RST_STREAM is the last frame that an endpoint can send on a stream.
> **MUST**: The peer that sends the RST_STREAM frame MUST be prepared to receive
   any frames that were sent or enqueued for sending by the remote peer.
   These frames can be ignored, except where they modify connection
   state (such as the state maintained for field section compression
   (Section 4.3) or flow control).

> **SHOULD NOT**: Normally, an endpoint SHOULD NOT send more than one RST_STREAM frame
   for any stream.  However, an endpoint MAY send additional RST_STREAM
   frames if it receives frames on a closed stream after more than a
   round-trip time.  This behavior is permitted to deal with misbehaving
   implementations.

> **MUST NOT**: To avoid looping, an endpoint MUST NOT send a RST_STREAM in response
   to a RST_STREAM frame.

### 5.4.3  Connection Termination

   If the TCP connection is closed or reset while streams remain in the
   "open" or "half-closed" states, then the affected streams cannot be
   automatically retried (see Section 8.7 for details).

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
