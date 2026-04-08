---
title: "6.5.  SETTINGS"
rfc_number: 9113
rfc_section: "6.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 6.5: SETTINGS — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE]
---

## 6.5.  SETTINGS

## 6.5  SETTINGS

   The SETTINGS frame (type=0x04) conveys configuration parameters that
   affect how endpoints communicate, such as preferences and constraints
   on peer behavior.  The SETTINGS frame is also used to acknowledge the
   receipt of those settings.  Individually, a configuration parameter
   from a SETTINGS frame is referred to as a "setting".

   Settings are not negotiated; they describe characteristics of the
   sending peer, which are used by the receiving peer.  Different values
   for the same setting can be advertised by each peer.  For example, a
   client might set a high initial flow-control window, whereas a server
   might set a lower value to conserve resources.

> **MUST**: A SETTINGS frame MUST be sent by both endpoints at the start of a
   connection and MAY be sent at any other time by either endpoint over
> **MUST**: the lifetime of the connection.  Implementations MUST support all of
   the settings defined by this specification.

   Each parameter in a SETTINGS frame replaces any existing value for
   that parameter.  Settings are processed in the order in which they
   appear, and a receiver of a SETTINGS frame does not need to maintain
   any state other than the current value of each setting.  Therefore,
   the value of a SETTINGS parameter is the last value that is seen by a
   receiver.

   SETTINGS frames are acknowledged by the receiving peer.  To enable
   this, the SETTINGS frame defines the ACK flag:

   ACK (0x01):  When set, the ACK flag indicates that this frame
      acknowledges receipt and application of the peer's SETTINGS frame.
> **MUST**: When this bit is set, the frame payload of the SETTINGS frame MUST
      be empty.  Receipt of a SETTINGS frame with the ACK flag set and a
> **MUST**: length field value other than 0 MUST be treated as a connection
      error (Section 5.4.1) of type FRAME_SIZE_ERROR.  For more
      information, see Section 6.5.3 ("Settings Synchronization").

   SETTINGS frames always apply to a connection, never a single stream.
> **MUST**: The stream identifier for a SETTINGS frame MUST be zero (0x00).  If
   an endpoint receives a SETTINGS frame whose Stream Identifier field
> **MUST**: is anything other than 0x00, the endpoint MUST respond with a
   connection error (Section 5.4.1) of type PROTOCOL_ERROR.

   The SETTINGS frame affects connection state.  A badly formed or
> **MUST**: incomplete SETTINGS frame MUST be treated as a connection error
   (Section 5.4.1) of type PROTOCOL_ERROR.

> **MUST**: A SETTINGS frame with a length other than a multiple of 6 octets MUST
   be treated as a connection error (Section 5.4.1) of type
   FRAME_SIZE_ERROR.

### 6.5.1  SETTINGS Format

   The frame payload of a SETTINGS frame consists of zero or more
   settings, each consisting of an unsigned 16-bit setting identifier
   and an unsigned 32-bit value.

   SETTINGS Frame {
     Length (24),
     Type (8) = 0x04,

     Unused Flags (7),
     ACK Flag (1),

     Reserved (1),
     Stream Identifier (31) = 0,

     Setting (48) ...,
   }

   Setting {
     Identifier (16),
     Value (32),
   }

                      Figure 7: SETTINGS Frame Format

   The Length, Type, Unused Flag(s), Reserved, and Stream Identifier
   fields are described in Section 4.  The frame payload of a SETTINGS
   frame contains any number of Setting fields, each of which consists
   of:

   Identifier:  A 16-bit setting identifier; see Section 6.5.2.

   Value:  A 32-bit value for the setting.

### 6.5.2  Defined Settings

   The following settings are defined:

   SETTINGS_HEADER_TABLE_SIZE (0x01):  This setting allows the sender to
      inform the remote endpoint of the maximum size of the compression
      table used to decode field blocks, in units of octets.  The
      encoder can select any size equal to or less than this value by
      using signaling specific to the compression format inside a field
      block (see [COMPRESSION]).  The initial value is 4,096 octets.

   SETTINGS_ENABLE_PUSH (0x02):  This setting can be used to enable or
> **MUST NOT**: disable server push.  A server MUST NOT send a PUSH_PROMISE frame
      if it receives this parameter set to a value of 0; see
      Section 8.4.  A client that has both set this parameter to 0 and
> **MUST**: had it acknowledged MUST treat the receipt of a PUSH_PROMISE frame
      as a connection error (Section 5.4.1) of type PROTOCOL_ERROR.

      The initial value of SETTINGS_ENABLE_PUSH is 1.  For a client,
      this value indicates that it is willing to receive PUSH_PROMISE
      frames.  For a server, this initial value has no effect, and is
> **MUST**: equivalent to the value 0.  Any value other than 0 or 1 MUST be
      treated as a connection error (Section 5.4.1) of type
      PROTOCOL_ERROR.

> **MUST NOT**: A server MUST NOT explicitly set this value to 1.  A server MAY
      choose to omit this setting when it sends a SETTINGS frame, but if
> **MUST**: a server does include a value, it MUST be 0.  A client MUST treat
      receipt of a SETTINGS frame with SETTINGS_ENABLE_PUSH set to 1 as
      a connection error (Section 5.4.1) of type PROTOCOL_ERROR.

   SETTINGS_MAX_CONCURRENT_STREAMS (0x03):  This setting indicates the
      maximum number of concurrent streams that the sender will allow.
      This limit is directional: it applies to the number of streams
      that the sender permits the receiver to create.  Initially, there
      is no limit to this value.  It is recommended that this value be
      no smaller than 100, so as to not unnecessarily limit parallelism.

> **SHOULD NOT**: A value of 0 for SETTINGS_MAX_CONCURRENT_STREAMS SHOULD NOT be
      treated as special by endpoints.  A zero value does prevent the
      creation of new streams; however, this can also happen for any
> **SHOULD**: limit that is exhausted with active streams.  Servers SHOULD only
      set a zero value for short durations; if a server does not wish to
      accept requests, closing the connection is more appropriate.

   SETTINGS_INITIAL_WINDOW_SIZE (0x04):  This setting indicates the
      sender's initial window size (in units of octets) for stream-level
      flow control.  The initial value is 2^16-1 (65,535) octets.

      This setting affects the window size of all streams (see
      Section 6.9.2).

> **MUST**: Values above the maximum flow-control window size of 2^31-1 MUST
      be treated as a connection error (Section 5.4.1) of type
      FLOW_CONTROL_ERROR.

   SETTINGS_MAX_FRAME_SIZE (0x05):  This setting indicates the size of
      the largest frame payload that the sender is willing to receive,
      in units of octets.

      The initial value is 2^14 (16,384) octets.  The value advertised
> **MUST**: by an endpoint MUST be between this initial value and the maximum
      allowed frame size (2^24-1 or 16,777,215 octets), inclusive.
> **MUST**: Values outside this range MUST be treated as a connection error
      (Section 5.4.1) of type PROTOCOL_ERROR.

   SETTINGS_MAX_HEADER_LIST_SIZE (0x06):  This advisory setting informs
      a peer of the maximum field section size that the sender is
      prepared to accept, in units of octets.  The value is based on the
      uncompressed size of field lines, including the length of the name
      and value in units of octets plus an overhead of 32 octets for
      each field line.

> **MAY**: For any given request, a lower limit than what is advertised MAY
      be enforced.  The initial value of this setting is unlimited.

   An endpoint that receives a SETTINGS frame with any unknown or
> **MUST**: unsupported identifier MUST ignore that setting.

### 6.5.3  Settings Synchronization

   Most values in SETTINGS benefit from or require an understanding of
   when the peer has received and applied the changed parameter values.
   In order to provide such synchronization timepoints, the recipient of
> **MUST**: a SETTINGS frame in which the ACK flag is not set MUST apply the
   updated settings as soon as possible upon receipt.  SETTINGS frames
   are acknowledged in the order in which they are received.

> **MUST**: The values in the SETTINGS frame MUST be processed in the order they
   appear, with no other frame processing between values.  Unsupported
> **MUST**: settings MUST be ignored.  Once all values have been processed, the
   recipient MUST immediately emit a SETTINGS frame with the ACK flag
   set.  Upon receiving a SETTINGS frame with the ACK flag set, the
   sender of the altered settings can rely on the values from the oldest
   unacknowledged SETTINGS frame having been applied.

   If the sender of a SETTINGS frame does not receive an acknowledgment
> **MAY**: within a reasonable amount of time, it MAY issue a connection error
   (Section 5.4.1) of type SETTINGS_TIMEOUT.  In setting a timeout, some
   allowance needs to be made for processing delays at the peer; a
   timeout that is solely based on the round-trip time between endpoints
   might result in spurious errors.

---

## TurboHTTP Compliance

**Status**: ⚠️ Partial

### Implementation Notes

- **`Http2Settings.cs`** — Supports all 6 defined settings: `SETTINGS_HEADER_TABLE_SIZE` (0x01), `SETTINGS_ENABLE_PUSH` (0x02), `SETTINGS_MAX_CONCURRENT_STREAMS` (0x03), `SETTINGS_INITIAL_WINDOW_SIZE` (0x04), `SETTINGS_MAX_FRAME_SIZE` (0x05), `SETTINGS_MAX_HEADER_LIST_SIZE` (0x06)
- **`Http2FrameDecoder.cs`** — Validates SETTINGS frame: stream ID must be 0, length must be multiple of 6, ACK frame must be empty per §6.5
- **`Http2Connection.cs`** — Sends SETTINGS at connection start per §6.5; processes settings in order per §6.5.3; sends ACK after applying received settings
- **`Http2SettingsValidator.cs`** — Validates setting values: `SETTINGS_ENABLE_PUSH` must be 0 or 1, `SETTINGS_INITIAL_WINDOW_SIZE` ≤ 2^31-1, `SETTINGS_MAX_FRAME_SIZE` between 2^14 and 2^24-1

### Test References

- `TurboHTTP.Tests/RFC9113/10_Http2SettingsTests.cs` — Settings encoding/decoding, value validation
- `TurboHTTP.Tests/RFC9113/11_Http2SettingsAckTests.cs` — ACK synchronization, timeout handling
- `TurboHTTP.Tests/RFC9113/12_Http2SettingsErrorTests.cs` — Invalid settings detection (bad stream ID, wrong length, invalid values)

### Known Gaps

- ⚠️ SETTINGS ACK timeout (§6.5.3) — no `SETTINGS_TIMEOUT` error is raised if peer doesn't acknowledge within reasonable time; relies on connection-level timeout instead
- ⚠️ `SETTINGS_ENABLE_PUSH` — always sent as 0 (push disabled) but server's push-related frames are not fully validated against this setting

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
