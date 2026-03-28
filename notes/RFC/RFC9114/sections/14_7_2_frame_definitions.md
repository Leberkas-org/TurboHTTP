---
title: "7.2.  Frame Definitions"
rfc_number: 9114
rfc_section: "7.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 7.2: Frame Definitions — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, frame_definitions]
---

## 7.2.  Frame Definitions

## 7.2  Frame Definitions

### 7.2.1  DATA

   DATA frames (type=0x00) convey arbitrary, variable-length sequences
   of bytes associated with HTTP request or response content.

> **MUST**: DATA frames MUST be associated with an HTTP request or response.  If
   a DATA frame is received on a control stream, the recipient MUST
   respond with a connection error of type H3_FRAME_UNEXPECTED.

   DATA Frame {
     Type (i) = 0x00,
     Length (i),
     Data (..),
   }

                            Figure 4: DATA Frame

### 7.2.2  HEADERS

   The HEADERS frame (type=0x01) is used to carry an HTTP field section
   that is encoded using QPACK.  See [QPACK] for more details.

   HEADERS Frame {
     Type (i) = 0x01,
     Length (i),
     Encoded Field Section (..),
   }

                          Figure 5: HEADERS Frame

   HEADERS frames can only be sent on request streams or push streams.
   If a HEADERS frame is received on a control stream, the recipient
> **MUST**: MUST respond with a connection error of type H3_FRAME_UNEXPECTED.

### 7.2.3  CANCEL_PUSH

   The CANCEL_PUSH frame (type=0x03) is used to request cancellation of
   a server push prior to the push stream being received.  The
   CANCEL_PUSH frame identifies a server push by push ID (see
   Section 4.6), encoded as a variable-length integer.

   When a client sends a CANCEL_PUSH frame, it is indicating that it
> **SHOULD**: does not wish to receive the promised resource.  The server SHOULD
   abort sending the resource, but the mechanism to do so depends on the
   state of the corresponding push stream.  If the server has not yet
   created a push stream, it does not create one.  If the push stream is
> **SHOULD**: open, the server SHOULD abruptly terminate that stream.  If the push
   stream has already ended, the server MAY still abruptly terminate the
> **MAY**: stream or MAY take no action.

   A server sends a CANCEL_PUSH frame to indicate that it will not be
   fulfilling a promise that was previously sent.  The client cannot
   expect the corresponding promise to be fulfilled, unless it has
   already received and processed the promised response.  Regardless of
> **SHOULD**: whether a push stream has been opened, a server SHOULD send a
   CANCEL_PUSH frame when it determines that promise will not be
   fulfilled.  If a stream has already been opened, the server can abort
   sending on the stream with an error code of H3_REQUEST_CANCELLED.

   Sending a CANCEL_PUSH frame has no direct effect on the state of
> **SHOULD NOT**: existing push streams.  A client SHOULD NOT send a CANCEL_PUSH frame
   when it has already received a corresponding push stream.  A push
   stream could arrive after a client has sent a CANCEL_PUSH frame,
   because a server might not have processed the CANCEL_PUSH.  The
> **SHOULD**: client SHOULD abort reading the stream with an error code of
   H3_REQUEST_CANCELLED.

   A CANCEL_PUSH frame is sent on the control stream.  Receiving a
> **MUST**: CANCEL_PUSH frame on a stream other than the control stream MUST be
   treated as a connection error of type H3_FRAME_UNEXPECTED.

   CANCEL_PUSH Frame {
     Type (i) = 0x03,
     Length (i),
     Push ID (i),
   }

                        Figure 6: CANCEL_PUSH Frame

   The CANCEL_PUSH frame carries a push ID encoded as a variable-length
   integer.  The Push ID field identifies the server push that is being
   cancelled; see Section 4.6.  If a CANCEL_PUSH frame is received that
   references a push ID greater than currently allowed on the
> **MUST**: connection, this MUST be treated as a connection error of type
   H3_ID_ERROR.

   If the client receives a CANCEL_PUSH frame, that frame might identify
   a push ID that has not yet been mentioned by a PUSH_PROMISE frame due
   to reordering.  If a server receives a CANCEL_PUSH frame for a push
> **MUST**: ID that has not yet been mentioned by a PUSH_PROMISE frame, this MUST
   be treated as a connection error of type H3_ID_ERROR.

### 7.2.4  SETTINGS

   The SETTINGS frame (type=0x04) conveys configuration parameters that
   affect how endpoints communicate, such as preferences and constraints
   on peer behavior.  Individually, a SETTINGS parameter can also be
   referred to as a "setting"; the identifier and value of each setting
   parameter can be referred to as a "setting identifier" and a "setting
   value".

   SETTINGS frames always apply to an entire HTTP/3 connection, never a
> **MUST**: single stream.  A SETTINGS frame MUST be sent as the first frame of
   each control stream (see Section 6.2.1) by each peer, and it MUST NOT
   be sent subsequently.  If an endpoint receives a second SETTINGS
> **MUST**: frame on the control stream, the endpoint MUST respond with a
   connection error of type H3_FRAME_UNEXPECTED.

> **MUST NOT**: SETTINGS frames MUST NOT be sent on any stream other than the control
   stream.  If an endpoint receives a SETTINGS frame on a different
> **MUST**: stream, the endpoint MUST respond with a connection error of type
   H3_FRAME_UNEXPECTED.

   SETTINGS parameters are not negotiated; they describe characteristics
   of the sending peer that can be used by the receiving peer.  However,
   a negotiation can be implied by the use of SETTINGS: each peer uses
   SETTINGS to advertise a set of supported values.  The definition of
   the setting would describe how each peer combines the two sets to
   conclude which choice will be used.  SETTINGS does not provide a
   mechanism to identify when the choice takes effect.

   Different values for the same parameter can be advertised by each
   peer.  For example, a client might be willing to consume a very large
   response field section, while servers are more cautious about request
   size.

> **MUST NOT**: The same setting identifier MUST NOT occur more than once in the
   SETTINGS frame.  A receiver MAY treat the presence of duplicate
   setting identifiers as a connection error of type H3_SETTINGS_ERROR.

   The payload of a SETTINGS frame consists of zero or more parameters.
   Each parameter consists of a setting identifier and a value, both
   encoded as QUIC variable-length integers.

   Setting {
     Identifier (i),
     Value (i),
   }

   SETTINGS Frame {
     Type (i) = 0x04,
     Length (i),
     Setting (..) ...,
   }

                          Figure 7: SETTINGS Frame

> **MUST**: An implementation MUST ignore any parameter with an identifier it
   does not understand.

#### 7.2.4.1  Defined SETTINGS Parameters

   The following settings are defined in HTTP/3:

   SETTINGS_MAX_FIELD_SECTION_SIZE (0x06):  The default value is
      unlimited.  See Section 4.2.2 for usage.

   Setting identifiers of the format 0x1f * N + 0x21 for non-negative
   integer values of N are reserved to exercise the requirement that
   unknown identifiers be ignored.  Such settings have no defined
> **SHOULD**: meaning.  Endpoints SHOULD include at least one such setting in their
   SETTINGS frame.  Endpoints MUST NOT consider such settings to have
   any meaning upon receipt.

   Because the setting has no defined meaning, the value of the setting
   can be any value the implementation selects.

   Setting identifiers that were defined in [HTTP/2] where there is no
   corresponding HTTP/3 setting have also been reserved
> **MUST NOT**: (Section 11.2.2).  These reserved settings MUST NOT be sent, and
   their receipt MUST be treated as a connection error of type
   H3_SETTINGS_ERROR.

   Additional settings can be defined by extensions to HTTP/3; see
   Section 9 for more details.

#### 7.2.4.2  Initialization

> **MUST NOT**: An HTTP implementation MUST NOT send frames or requests that would be
   invalid based on its current understanding of the peer's settings.

> **SHOULD**: All settings begin at an initial value.  Each endpoint SHOULD use
   these initial values to send messages before the peer's SETTINGS
   frame has arrived, as packets carrying the settings can be lost or
   delayed.  When the SETTINGS frame arrives, any settings are changed
   to their new values.

   This removes the need to wait for the SETTINGS frame before sending
> **MUST NOT**: messages.  Endpoints MUST NOT require any data to be received from
   the peer prior to sending the SETTINGS frame; settings MUST be sent
   as soon as the transport is ready to send data.

   For servers, the initial value of each client setting is the default
   value.

   For clients using a 1-RTT QUIC connection, the initial value of each
   server setting is the default value.  1-RTT keys will always become
   available prior to the packet containing SETTINGS being processed by
> **SHOULD**: QUIC, even if the server sends SETTINGS immediately.  Clients SHOULD
   NOT wait indefinitely for SETTINGS to arrive before sending requests,
> **SHOULD**: but they SHOULD process received datagrams in order to increase the
   likelihood of processing SETTINGS before sending the first request.

   When a 0-RTT QUIC connection is being used, the initial value of each
   server setting is the value used in the previous session.  Clients
> **SHOULD**: SHOULD store the settings the server provided in the HTTP/3
   connection where resumption information was provided, but they MAY
   opt not to store settings in certain cases (e.g., if the session
> **MUST**: ticket is received before the SETTINGS frame).  A client MUST comply
   with stored settings -- or default values if no values are stored --
   when attempting 0-RTT.  Once a server has provided new settings,
> **MUST**: clients MUST comply with those values.

   A server can remember the settings that it advertised or store an
   integrity-protected copy of the values in the ticket and recover the
   information when accepting 0-RTT data.  A server uses the HTTP/3
   settings values in determining whether to accept 0-RTT data.  If the
   server cannot determine that the settings remembered by a client are
> **MUST NOT**: compatible with its current settings, it MUST NOT accept 0-RTT data.
   Remembered settings are compatible if a client complying with those
   settings would not violate the server's current settings.

> **MAY**: A server MAY accept 0-RTT and subsequently provide different settings
   in its SETTINGS frame.  If 0-RTT data is accepted by the server, its
> **MUST NOT**: SETTINGS frame MUST NOT reduce any limits or alter any values that
   might be violated by the client with its 0-RTT data.  The server MUST
   include all settings that differ from their default values.  If a
   server accepts 0-RTT but then sends settings that are not compatible
> **MUST**: with the previously specified settings, this MUST be treated as a
   connection error of type H3_SETTINGS_ERROR.  If a server accepts
   0-RTT but then sends a SETTINGS frame that omits a setting value that
   the client understands (apart from reserved setting identifiers) that
> **MUST**: was previously specified to have a non-default value, this MUST be
   treated as a connection error of type H3_SETTINGS_ERROR.

### 7.2.5  PUSH_PROMISE

   The PUSH_PROMISE frame (type=0x05) is used to carry a promised
   request header section from server to client on a request stream.

   PUSH_PROMISE Frame {
     Type (i) = 0x05,
     Length (i),
     Push ID (i),
     Encoded Field Section (..),
   }

                        Figure 8: PUSH_PROMISE Frame

   The payload consists of:

   Push ID:  A variable-length integer that identifies the server push
      operation.  A push ID is used in push stream headers (Section 4.6)
      and CANCEL_PUSH frames.

   Encoded Field Section:  QPACK-encoded request header fields for the
      promised response.  See [QPACK] for more details.

> **MUST NOT**: A server MUST NOT use a push ID that is larger than the client has
   provided in a MAX_PUSH_ID frame (Section 7.2.7).  A client MUST treat
   receipt of a PUSH_PROMISE frame that contains a larger push ID than
   the client has advertised as a connection error of H3_ID_ERROR.

> **MAY**: A server MAY use the same push ID in multiple PUSH_PROMISE frames.
   If so, the decompressed request header sets MUST contain the same
   fields in the same order, and both the name and the value in each
> **MUST**: field MUST be exact matches.  Clients SHOULD compare the request
   header sections for resources promised multiple times.  If a client
   receives a push ID that has already been promised and detects a
> **MUST**: mismatch, it MUST respond with a connection error of type
   H3_GENERAL_PROTOCOL_ERROR.  If the decompressed field sections match
> **SHOULD**: exactly, the client SHOULD associate the pushed content with each
   stream on which a PUSH_PROMISE frame was received.

   Allowing duplicate references to the same push ID is primarily to
> **SHOULD**: reduce duplication caused by concurrent requests.  A server SHOULD
   avoid reusing a push ID over a long period.  Clients are likely to
   consume server push responses and not retain them for reuse over
   time.  Clients that see a PUSH_PROMISE frame that uses a push ID that
   they have already consumed and discarded are forced to ignore the
   promise.

   If a PUSH_PROMISE frame is received on the control stream, the client
> **MUST**: MUST respond with a connection error of type H3_FRAME_UNEXPECTED.

> **MUST NOT**: A client MUST NOT send a PUSH_PROMISE frame.  A server MUST treat the
   receipt of a PUSH_PROMISE frame as a connection error of type
   H3_FRAME_UNEXPECTED.

   See Section 4.6 for a description of the overall server push
   mechanism.

### 7.2.6  GOAWAY

   The GOAWAY frame (type=0x07) is used to initiate graceful shutdown of
   an HTTP/3 connection by either endpoint.  GOAWAY allows an endpoint
   to stop accepting new requests or pushes while still finishing
   processing of previously received requests and pushes.  This enables
   administrative actions, like server maintenance.  GOAWAY by itself
   does not close a connection.

   GOAWAY Frame {
     Type (i) = 0x07,
     Length (i),
     Stream ID/Push ID (i),
   }

                           Figure 9: GOAWAY Frame

   The GOAWAY frame is always sent on the control stream.  In the
   server-to-client direction, it carries a QUIC stream ID for a client-
   initiated bidirectional stream encoded as a variable-length integer.
> **MUST**: A client MUST treat receipt of a GOAWAY frame containing a stream ID
   of any other type as a connection error of type H3_ID_ERROR.

   In the client-to-server direction, the GOAWAY frame carries a push ID
   encoded as a variable-length integer.

   The GOAWAY frame applies to the entire connection, not a specific
> **MUST**: stream.  A client MUST treat a GOAWAY frame on a stream other than
   the control stream as a connection error of type H3_FRAME_UNEXPECTED.

   See Section 5.2 for more information on the use of the GOAWAY frame.

### 7.2.7  MAX_PUSH_ID

   The MAX_PUSH_ID frame (type=0x0d) is used by clients to control the
   number of server pushes that the server can initiate.  This sets the
   maximum value for a push ID that the server can use in PUSH_PROMISE
   and CANCEL_PUSH frames.  Consequently, this also limits the number of
   push streams that the server can initiate in addition to the limit
   maintained by the QUIC transport.

   The MAX_PUSH_ID frame is always sent on the control stream.  Receipt
> **MUST**: of a MAX_PUSH_ID frame on any other stream MUST be treated as a
   connection error of type H3_FRAME_UNEXPECTED.

> **MUST NOT**: A server MUST NOT send a MAX_PUSH_ID frame.  A client MUST treat the
   receipt of a MAX_PUSH_ID frame as a connection error of type
   H3_FRAME_UNEXPECTED.

   The maximum push ID is unset when an HTTP/3 connection is created,
   meaning that a server cannot push until it receives a MAX_PUSH_ID
   frame.  A client that wishes to manage the number of promised server
   pushes can increase the maximum push ID by sending MAX_PUSH_ID frames
   as the server fulfills or cancels server pushes.

   MAX_PUSH_ID Frame {
     Type (i) = 0x0d,
     Length (i),
     Push ID (i),
   }

                        Figure 10: MAX_PUSH_ID Frame

   The MAX_PUSH_ID frame carries a single variable-length integer that
   identifies the maximum value for a push ID that the server can use;
   see Section 4.6.  A MAX_PUSH_ID frame cannot reduce the maximum push
   ID; receipt of a MAX_PUSH_ID frame that contains a smaller value than
> **MUST**: previously received MUST be treated as a connection error of type
   H3_ID_ERROR.

### 7.2.8  Reserved Frame Types

   Frame types of the format 0x1f * N + 0x21 for non-negative integer
   values of N are reserved to exercise the requirement that unknown
   types be ignored (Section 9).  These frames have no semantics, and
> **MAY**: they MAY be sent on any stream where frames are allowed to be sent.
   This enables their use for application-layer padding.  Endpoints MUST
   NOT consider these frames to have any meaning upon receipt.

   The payload and length of the frames are selected in any manner the
   implementation chooses.

   Frame types that were used in HTTP/2 where there is no corresponding
   HTTP/3 frame have also been reserved (Section 11.2.1)

---

## TurboHttp Compliance

**Status**: ⚠️ Partial

### Implementation Notes

- **`Http3FrameDecoder.cs`** — Decodes all 7 defined frame types: DATA (0x00), HEADERS (0x01), CANCEL_PUSH (0x03), SETTINGS (0x04), PUSH_PROMISE (0x05), GOAWAY (0x07), MAX_PUSH_ID (0x0d)
- **`Http3FrameEncoder.cs`** — Encodes DATA, HEADERS, SETTINGS, and GOAWAY frames; validates stream-type restrictions
- **`Http3Settings.cs`** — Full SETTINGS frame: `SETTINGS_MAX_FIELD_SECTION_SIZE`, reserved ID handling, duplicate detection, HTTP/2 setting rejection per §7.2.4
- **`Http3GoAwayHandler.cs`** — GOAWAY processing with decreasing stream/push ID validation per §7.2.6
- **`Http3ErrorCodes.cs`** — All 16 HTTP/3 error codes (0x0100–0x0110)

### Test References

- `TurboHttp.Tests/RFC9114/01_Http3FrameDecoderTests.cs` — Frame type dispatch and payload parsing
- `TurboHttp.Tests/RFC9114/02_Http3FrameEncoderTests.cs` — Encoding round-trips
- `TurboHttp.Tests/RFC9114/07_Http3SettingsTests.cs` — SETTINGS validation
- `TurboHttp.Tests/RFC9114/08_Http3GoAwayTests.cs` — GOAWAY frame processing

### Known Gaps

- ❌ CANCEL_PUSH (§7.2.3) — decoded but not acted upon (server push not implemented)
- ❌ PUSH_PROMISE (§7.2.5) — rejected with `H3_FRAME_UNEXPECTED` but push ID validation minimal
- ❌ MAX_PUSH_ID (§7.2.7) — not sent by client; server receipt correctly rejected
- ⚠️ Reserved frame types (§7.2.8) — ignored on receipt but not sent for padding.  These frame
> **MUST NOT**: types MUST NOT be sent, and their receipt MUST be treated as a
   connection error of type H3_FRAME_UNEXPECTED.

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
