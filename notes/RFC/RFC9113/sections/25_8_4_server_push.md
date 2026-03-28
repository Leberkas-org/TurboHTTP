---
title: "8.4.  Server Push"
rfc_number: 9113
rfc_section: "8.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 8.4: Server Push — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, server_push]
---

## 8.4.  Server Push

## 8.4  Server Push

   HTTP/2 allows a server to preemptively send (or "push") responses
   (along with corresponding "promised" requests) to a client in
   association with a previous client-initiated request.

   Server push was designed to allow a server to improve client-
   perceived performance by predicting what requests will follow those
   that it receives, thereby removing a round trip for them.  For
   example, a request for HTML is often followed by requests for
   stylesheets and scripts referenced by that page.  When these requests
   are pushed, the client does not need to wait to receive the
   references to them in the HTML and issue separate requests.

   In practice, server push is difficult to use effectively, because it
   requires the server to correctly anticipate the additional requests
   the client will make, taking into account factors such as caching,
   content negotiation, and user behavior.  Errors in prediction can
   lead to performance degradation, due to the opportunity cost that the
   additional data on the wire represents.  In particular, pushing any
   significant amount of data can cause contention issues with responses
   that are more important.

   A client can request that server push be disabled, though this is
   negotiated for each hop independently.  The SETTINGS_ENABLE_PUSH
   setting can be set to 0 to indicate that server push is disabled.

> **MUST**: Promised requests MUST be safe (see Section 9.2.1 of [HTTP]) and
   cacheable (see Section 9.2.3 of [HTTP]).  Promised requests cannot
   include any content or a trailer section.  Clients that receive a
   promised request that is not cacheable, that is not known to be safe,
> **MUST**: or that indicates the presence of request content MUST reset the
   promised stream with a stream error (Section 5.4.2) of type
   PROTOCOL_ERROR.  Note that this could result in the promised stream
   being reset if the client does not recognize a newly defined method
   as being safe.

   Pushed responses that are cacheable (see Section 3 of [CACHING]) can
   be stored by the client, if it implements an HTTP cache.  Pushed
   responses are considered successfully validated on the origin server
   (e.g., if the "no-cache" cache response directive is present; see
   Section 5.2.2.4 of [CACHING]) while the stream identified by the
   promised stream identifier is still open.

> **MUST NOT**: Pushed responses that are not cacheable MUST NOT be stored by any
   HTTP cache.  They MAY be made available to the application
   separately.

> **MUST**: The server MUST include a value in the ":authority" pseudo-header
   field for which the server is authoritative (see Section 10.1).  A
> **MUST**: client MUST treat a PUSH_PROMISE for which the server is not
   authoritative as a stream error (Section 5.4.2) of type
   PROTOCOL_ERROR.

   An intermediary can receive pushes from the server and choose not to
   forward them on to the client.  In other words, how to make use of
   the pushed information is up to that intermediary.  Equally, the
   intermediary might choose to make additional pushes to the client,
   without any action taken by the server.

> **MUST**: A client cannot push.  Thus, servers MUST treat the receipt of a
   PUSH_PROMISE frame as a connection error (Section 5.4.1) of type
   PROTOCOL_ERROR.  A server cannot set the SETTINGS_ENABLE_PUSH setting
   to a value other than 0 (see Section 6.5.2).

### 8.4.1  Push Requests

   Server push is semantically equivalent to a server responding to a
   request; however, in this case, that request is also sent by the
   server, as a PUSH_PROMISE frame.

   The PUSH_PROMISE frame includes a field block that contains control
   data and a complete set of request header fields that the server
   attributes to the request.  It is not possible to push a response to
   a request that includes message content.

   Promised requests are always associated with an explicit request from
   the client.  The PUSH_PROMISE frames sent by the server are sent on
   that explicit request's stream.  The PUSH_PROMISE frame also includes
   a promised stream identifier, chosen from the stream identifiers
   available to the server (see Section 5.1.1).

   The header fields in PUSH_PROMISE and any subsequent CONTINUATION
> **MUST**: frames MUST be a valid and complete set of request header fields
   (Section 8.3.1).  The server MUST include a method in the ":method"
   pseudo-header field that is safe and cacheable.  If a client receives
   a PUSH_PROMISE that does not include a complete and valid set of
   header fields or the ":method" pseudo-header field identifies a
> **MUST**: method that is not safe, it MUST respond on the promised stream with
   a stream error (Section 5.4.2) of type PROTOCOL_ERROR.

> **SHOULD**: The server SHOULD send PUSH_PROMISE (Section 6.6) frames prior to
   sending any frames that reference the promised responses.  This
   avoids a race where clients issue requests prior to receiving any
   PUSH_PROMISE frames.

   For example, if the server receives a request for a document
   containing embedded links to multiple image files and the server
   chooses to push those additional images to the client, sending
   PUSH_PROMISE frames before the DATA frames that contain the image
   links ensures that the client is able to see that a resource will be
   pushed before discovering embedded links.  Similarly, if the server
   pushes resources referenced by the field block (for instance, in Link
   header fields), sending a PUSH_PROMISE before sending the header
   ensures that clients do not request those resources.

> **MUST NOT**: PUSH_PROMISE frames MUST NOT be sent by the client.

   PUSH_PROMISE frames can be sent by the server on any client-initiated
> **MUST**: stream, but the stream MUST be in either the "open" or "half-closed
   (remote)" state with respect to the server.  PUSH_PROMISE frames are
   interspersed with the frames that comprise a response, though they
   cannot be interspersed with HEADERS and CONTINUATION frames that
   comprise a single field block.

   Sending a PUSH_PROMISE frame creates a new stream and puts the stream
   into the "reserved (local)" state for the server and the "reserved
   (remote)" state for the client.

### 8.4.2  Push Responses

   After sending the PUSH_PROMISE frame, the server can begin delivering
   the pushed response as a response (Section 8.3.2) on a server-
   initiated stream that uses the promised stream identifier.  The
   server uses this stream to transmit an HTTP response, using the same
   sequence of frames as that defined in Section 8.1.  This stream
   becomes "half-closed" to the client (Section 5.1) after the initial
   HEADERS frame is sent.

   Once a client receives a PUSH_PROMISE frame and chooses to accept the
> **SHOULD NOT**: pushed response, the client SHOULD NOT issue any requests for the
   promised response until after the promised stream has closed.

   If the client determines, for any reason, that it does not wish to
   receive the pushed response from the server or if the server takes
   too long to begin sending the promised response, the client can send
   a RST_STREAM frame, using either the CANCEL or REFUSED_STREAM code
   and referencing the pushed stream's identifier.

   A client can use the SETTINGS_MAX_CONCURRENT_STREAMS setting to limit
   the number of responses that can be concurrently pushed by a server.
   Advertising a SETTINGS_MAX_CONCURRENT_STREAMS value of zero prevents
   the server from opening the streams necessary to push responses.
   However, this does not prevent the server from reserving streams
   using PUSH_PROMISE frames, because reserved streams do not count
   toward the concurrent stream limit.  Clients that do not wish to
   receive pushed resources need to reset any unwanted reserved streams
   or set SETTINGS_ENABLE_PUSH to 0.

> **MUST**: Clients receiving a pushed response MUST validate that either the
   server is authoritative (see Section 10.1) or the proxy that provided
   the pushed response is configured for the corresponding request.  For
   example, a server that offers a certificate for only the example.com
   DNS-ID (see [RFC6125]) is not permitted to push a response for
   <https://www.example.org/doc>.

   The response for a PUSH_PROMISE stream begins with a HEADERS frame,
   which immediately puts the stream into the "half-closed (remote)"
   state for the server and "half-closed (local)" state for the client,
   and ends with a frame with the END_STREAM flag set, which places the
   stream in the "closed" state.

      |  Note: The client never sends a frame with the END_STREAM flag
      |  set for a server push.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
