---
title: "Appendix A.  Considerations for Transitioning from HTTP/2"
rfc_number: 9114
rfc_section: "Appendix A"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Appendix A: Considerations for Transitioning from HTTP/2 — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, considerations_for_transitioning_from_http2]
---

## Appendix A.  Considerations for Transitioning from HTTP/2

Appendix A.  Considerations for Transitioning from HTTP/2

   HTTP/3 is strongly informed by HTTP/2, and it bears many
   similarities.  This section describes the approach taken to design
   HTTP/3, points out important differences from HTTP/2, and describes
   how to map HTTP/2 extensions into HTTP/3.

   HTTP/3 begins from the premise that similarity to HTTP/2 is
   preferable, but not a hard requirement.  HTTP/3 departs from HTTP/2
   where QUIC differs from TCP, either to take advantage of QUIC
   features (like streams) or to accommodate important shortcomings
   (such as a lack of total ordering).  While HTTP/3 is similar to
   HTTP/2 in key aspects, such as the relationship of requests and
   responses to streams, the details of the HTTP/3 design are
   substantially different from HTTP/2.

   Some important departures are noted in this section.

A.1.  Streams

   HTTP/3 permits use of a larger number of streams (2^62-1) than
   HTTP/2.  The same considerations about exhaustion of stream
   identifier space apply, though the space is significantly larger such
   that it is likely that other limits in QUIC are reached first, such
   as the limit on the connection flow-control window.

   In contrast to HTTP/2, stream concurrency in HTTP/3 is managed by
   QUIC.  QUIC considers a stream closed when all data has been received
   and sent data has been acknowledged by the peer.  HTTP/2 considers a
   stream closed when the frame containing the END_STREAM bit has been
   committed to the transport.  As a result, the stream for an
   equivalent exchange could remain "active" for a longer period of
   time.  HTTP/3 servers might choose to permit a larger number of
   concurrent client-initiated bidirectional streams to achieve
   equivalent concurrency to HTTP/2, depending on the expected usage
   patterns.

   In HTTP/2, only request and response bodies (the frame payload of
   DATA frames) are subject to flow control.  All HTTP/3 frames are sent
   on QUIC streams, so all frames on all streams are flow controlled in
   HTTP/3.

   Due to the presence of other unidirectional stream types, HTTP/3 does
   not rely exclusively on the number of concurrent unidirectional
   streams to control the number of concurrent in-flight pushes.
   Instead, HTTP/3 clients use the MAX_PUSH_ID frame to control the
   number of pushes received from an HTTP/3 server.

A.2.  HTTP Frame Types

   Many framing concepts from HTTP/2 can be elided on QUIC, because the
   transport deals with them.  Because frames are already on a stream,
   they can omit the stream number.  Because frames do not block
   multiplexing (QUIC's multiplexing occurs below this layer), the
   support for variable-maximum-length packets can be removed.  Because
   stream termination is handled by QUIC, an END_STREAM flag is not
   required.  This permits the removal of the Flags field from the
   generic frame layout.

   Frame payloads are largely drawn from [HTTP/2].  However, QUIC
   includes many features (e.g., flow control) that are also present in
   HTTP/2.  In these cases, the HTTP mapping does not re-implement them.
   As a result, several HTTP/2 frame types are not required in HTTP/3.
   Where an HTTP/2-defined frame is no longer used, the frame ID has
   been reserved in order to maximize portability between HTTP/2 and
   HTTP/3 implementations.  However, even frame types that appear in
   both mappings do not have identical semantics.

   Many of the differences arise from the fact that HTTP/2 provides an
   absolute ordering between frames across all streams, while QUIC
   provides this guarantee on each stream only.  As a result, if a frame
   type makes assumptions that frames from different streams will still
   be received in the order sent, HTTP/3 will break them.

   Some examples of feature adaptations are described below, as well as
   general guidance to extension frame implementors converting an HTTP/2
   extension to HTTP/3.

A.2.1.  Prioritization Differences

   HTTP/2 specifies priority assignments in PRIORITY frames and
   (optionally) in HEADERS frames.  HTTP/3 does not provide a means of
   signaling priority.

   Note that, while there is no explicit signaling for priority, this
   does not mean that prioritization is not important for achieving good
   performance.

A.2.2.  Field Compression Differences

   HPACK was designed with the assumption of in-order delivery.  A
   sequence of encoded field sections must arrive (and be decoded) at an
   endpoint in the same order in which they were encoded.  This ensures
   that the dynamic state at the two endpoints remains in sync.

   Because this total ordering is not provided by QUIC, HTTP/3 uses a
   modified version of HPACK, called QPACK.  QPACK uses a single
   unidirectional stream to make all modifications to the dynamic table,
   ensuring a total order of updates.  All frames that contain encoded
   fields merely reference the table state at a given time without
   modifying it.

   [QPACK] provides additional details.

A.2.3.  Flow-Control Differences

   HTTP/2 specifies a stream flow-control mechanism.  Although all
   HTTP/2 frames are delivered on streams, only the DATA frame payload
   is subject to flow control.  QUIC provides flow control for stream
   data and all HTTP/3 frame types defined in this document are sent on
   streams.  Therefore, all frame headers and payload are subject to
   flow control.

A.2.4.  Guidance for New Frame Type Definitions

   Frame type definitions in HTTP/3 often use the QUIC variable-length
   integer encoding.  In particular, stream IDs use this encoding, which
   allows for a larger range of possible values than the encoding used
   in HTTP/2.  Some frames in HTTP/3 use an identifier other than a
   stream ID (e.g., push IDs).  Redefinition of the encoding of
   extension frame types might be necessary if the encoding includes a
   stream ID.

   Because the Flags field is not present in generic HTTP/3 frames,
   those frames that depend on the presence of flags need to allocate
   space for flags as part of their frame payload.

   Other than these issues, frame type HTTP/2 extensions are typically
   portable to QUIC simply by replacing stream 0 in HTTP/2 with a
   control stream in HTTP/3.  HTTP/3 extensions will not assume
   ordering, but would not be harmed by ordering, and are expected to be
   portable to HTTP/2.

A.2.5.  Comparison of HTTP/2 and HTTP/3 Frame Types

   DATA (0x00):  Padding is not defined in HTTP/3 frames.  See
      Section 7.2.1.

   HEADERS (0x01):  The PRIORITY region of HEADERS is not defined in
      HTTP/3 frames.  Padding is not defined in HTTP/3 frames.  See
      Section 7.2.2.

   PRIORITY (0x02):  As described in Appendix A.2.1, HTTP/3 does not
      provide a means of signaling priority.

   RST_STREAM (0x03):  RST_STREAM frames do not exist in HTTP/3, since
      QUIC provides stream lifecycle management.  The same code point is
      used for the CANCEL_PUSH frame (Section 7.2.3).

   SETTINGS (0x04):  SETTINGS frames are sent only at the beginning of
      the connection.  See Section 7.2.4 and Appendix A.3.

   PUSH_PROMISE (0x05):  The PUSH_PROMISE frame does not reference a
      stream; instead, the push stream references the PUSH_PROMISE frame
      using a push ID.  See Section 7.2.5.

   PING (0x06):  PING frames do not exist in HTTP/3, as QUIC provides
      equivalent functionality.

   GOAWAY (0x07):  GOAWAY does not contain an error code.  In the
      client-to-server direction, it carries a push ID instead of a
      server-initiated stream ID.  See Section 7.2.6.

   WINDOW_UPDATE (0x08):  WINDOW_UPDATE frames do not exist in HTTP/3,
      since QUIC provides flow control.

   CONTINUATION (0x09):  CONTINUATION frames do not exist in HTTP/3;
      instead, larger HEADERS/PUSH_PROMISE frames than HTTP/2 are
      permitted.

   Frame types defined by extensions to HTTP/2 need to be separately
   registered for HTTP/3 if still applicable.  The IDs of frames defined
   in [HTTP/2] have been reserved for simplicity.  Note that the frame
   type space in HTTP/3 is substantially larger (62 bits versus 8 bits),
   so many HTTP/3 frame types have no equivalent HTTP/2 code points.
   See Section 11.2.1.

A.3.  HTTP/2 SETTINGS Parameters

   An important difference from HTTP/2 is that settings are sent once,
   as the first frame of the control stream, and thereafter cannot
   change.  This eliminates many corner cases around synchronization of
   changes.

   Some transport-level options that HTTP/2 specifies via the SETTINGS
   frame are superseded by QUIC transport parameters in HTTP/3.  The
   HTTP-level setting that is retained in HTTP/3 has the same value as
   in HTTP/2.  The superseded settings are reserved, and their receipt
   is an error.  See Section 7.2.4.1 for discussion of both the retained
   and reserved values.

   Below is a listing of how each HTTP/2 SETTINGS parameter is mapped:

   SETTINGS_HEADER_TABLE_SIZE (0x01):  See [QPACK].

   SETTINGS_ENABLE_PUSH (0x02):  This is removed in favor of the
      MAX_PUSH_ID frame, which provides a more granular control over
      server push.  Specifying a setting with the identifier 0x02
      (corresponding to the SETTINGS_ENABLE_PUSH parameter) in the
      HTTP/3 SETTINGS frame is an error.

   SETTINGS_MAX_CONCURRENT_STREAMS (0x03):  QUIC controls the largest
      open stream ID as part of its flow-control logic.  Specifying a
      setting with the identifier 0x03 (corresponding to the
      SETTINGS_MAX_CONCURRENT_STREAMS parameter) in the HTTP/3 SETTINGS
      frame is an error.

   SETTINGS_INITIAL_WINDOW_SIZE (0x04):  QUIC requires both stream and
      connection flow-control window sizes to be specified in the
      initial transport handshake.  Specifying a setting with the
      identifier 0x04 (corresponding to the SETTINGS_INITIAL_WINDOW_SIZE
      parameter) in the HTTP/3 SETTINGS frame is an error.

   SETTINGS_MAX_FRAME_SIZE (0x05):  This setting has no equivalent in
      HTTP/3.  Specifying a setting with the identifier 0x05
      (corresponding to the SETTINGS_MAX_FRAME_SIZE parameter) in the
      HTTP/3 SETTINGS frame is an error.

   SETTINGS_MAX_HEADER_LIST_SIZE (0x06):  This setting identifier has
      been renamed SETTINGS_MAX_FIELD_SECTION_SIZE.

   In HTTP/3, setting values are variable-length integers (6, 14, 30, or
   62 bits long) rather than fixed-length 32-bit fields as in HTTP/2.
   This will often produce a shorter encoding, but can produce a longer
   encoding for settings that use the full 32-bit space.  Settings
   ported from HTTP/2 might choose to redefine their value to limit it
   to 30 bits for more efficient encoding or to make use of the 62-bit
   space if more than 30 bits are required.

   Settings need to be defined separately for HTTP/2 and HTTP/3.  The
   IDs of settings defined in [HTTP/2] have been reserved for
   simplicity.  Note that the settings identifier space in HTTP/3 is
   substantially larger (62 bits versus 16 bits), so many HTTP/3
   settings have no equivalent HTTP/2 code point.  See Section 11.2.2.

   As QUIC streams might arrive out of order, endpoints are advised not
   to wait for the peers' settings to arrive before responding to other
   streams.  See Section 7.2.4.2.

A.4.  HTTP/2 Error Codes

   QUIC has the same concepts of "stream" and "connection" errors that
   HTTP/2 provides.  However, the differences between HTTP/2 and HTTP/3
   mean that error codes are not directly portable between versions.

   The HTTP/2 error codes defined in Section 7 of [HTTP/2] logically map
   to the HTTP/3 error codes as follows:

   NO_ERROR (0x00):  H3_NO_ERROR in Section 8.1.

   PROTOCOL_ERROR (0x01):  This is mapped to H3_GENERAL_PROTOCOL_ERROR
      except in cases where more specific error codes have been defined.
      Such cases include H3_FRAME_UNEXPECTED, H3_MESSAGE_ERROR, and
      H3_CLOSED_CRITICAL_STREAM defined in Section 8.1.

   INTERNAL_ERROR (0x02):  H3_INTERNAL_ERROR in Section 8.1.

   FLOW_CONTROL_ERROR (0x03):  Not applicable, since QUIC handles flow
      control.

   SETTINGS_TIMEOUT (0x04):  Not applicable, since no acknowledgment of
      SETTINGS is defined.

   STREAM_CLOSED (0x05):  Not applicable, since QUIC handles stream
      management.

   FRAME_SIZE_ERROR (0x06):  H3_FRAME_ERROR error code defined in
      Section 8.1.

   REFUSED_STREAM (0x07):  H3_REQUEST_REJECTED (in Section 8.1) is used
      to indicate that a request was not processed.  Otherwise, not
      applicable because QUIC handles stream management.

   CANCEL (0x08):  H3_REQUEST_CANCELLED in Section 8.1.

   COMPRESSION_ERROR (0x09):  Multiple error codes are defined in
      [QPACK].

   CONNECT_ERROR (0x0a):  H3_CONNECT_ERROR in Section 8.1.

   ENHANCE_YOUR_CALM (0x0b):  H3_EXCESSIVE_LOAD in Section 8.1.

   INADEQUATE_SECURITY (0x0c):  Not applicable, since QUIC is assumed to
      provide sufficient security on all connections.

   HTTP_1_1_REQUIRED (0x0d):  H3_VERSION_FALLBACK in Section 8.1.

   Error codes need to be defined for HTTP/2 and HTTP/3 separately.  See
   Section 11.2.3.

A.4.1.  Mapping between HTTP/2 and HTTP/3 Errors

   An intermediary that converts between HTTP/2 and HTTP/3 may encounter
   error conditions from either upstream.  It is useful to communicate
   the occurrence of errors to the downstream, but error codes largely
   reflect connection-local problems that generally do not make sense to
   propagate.

   An intermediary that encounters an error from an upstream origin can
   indicate this by sending an HTTP status code such as 502 (Bad
   Gateway), which is suitable for a broad class of errors.

   There are some rare cases where it is beneficial to propagate the
   error by mapping it to the closest matching error type to the
   receiver.  For example, an intermediary that receives an HTTP/2
   stream error of type REFUSED_STREAM from the origin has a clear
   signal that the request was not processed and that the request is
   safe to retry.  Propagating this error condition to the client as an
   HTTP/3 stream error of type H3_REQUEST_REJECTED allows the client to
   take the action it deems most appropriate.  In the reverse direction,
   the intermediary might deem it beneficial to pass on client request
   cancellations that are indicated by terminating a stream with
   H3_REQUEST_CANCELLED; see Section 4.1.1.

   Conversion between errors is described in the logical mapping.  The
   error codes are defined in non-overlapping spaces in order to protect
   against accidental conversion that could result in the use of
   inappropriate or unknown error codes for the target version.  An
   intermediary is permitted to promote stream errors to connection
   errors but they should be aware of the cost to the HTTP/3 connection
   for what might be a temporary or intermittent error.

Acknowledgments

   Robbie Shade and Mike Warres were the authors of draft-shade-quic-
   http2-mapping, a precursor of this document.

   The IETF QUIC Working Group received an enormous amount of support
   from many people.  Among others, the following people provided
   substantial contributions to this document:

   *  Bence Beky
   *  Daan De Meyer
   *  Martin Duke
   *  Roy Fielding
   *  Alan Frindell
   *  Alessandro Ghedini
   *  Nick Harper
   *  Ryan Hamilton
   *  Christian Huitema
   *  Subodh Iyengar
   *  Robin Marx
   *  Patrick McManus
   *  Luca Niccolini
   *  奥 一穂 (Kazuho Oku)
   *  Lucas Pardue
   *  Roberto Peon
   *  Julian Reschke
   *  Eric Rescorla
   *  Martin Seemann
   *  Ben Schwartz
   *  Ian Swett
   *  Willy Taureau
   *  Martin Thomson
   *  Dmitri Tikhonov
   *  Tatsuhiro Tsujikawa

   A portion of Mike Bishop's contribution was supported by Microsoft
   during his employment there.

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
