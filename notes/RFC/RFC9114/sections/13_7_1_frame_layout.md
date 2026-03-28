---
title: "7.1.  Frame Layout"
rfc_number: 9114
rfc_section: "7.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 7.1: Frame Layout — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, frame_layout]
---

## 7.1.  Frame Layout

7.  HTTP Framing Layer

   HTTP frames are carried on QUIC streams, as described in Section 6.
   HTTP/3 defines three stream types: control stream, request stream,
   and push stream.  This section describes HTTP/3 frame formats and
   their permitted stream types; see Table 1 for an overview.  A
   comparison between HTTP/2 and HTTP/3 frames is provided in
   Appendix A.2.

   +==============+================+================+========+=========+
   | Frame        | Control Stream | Request        | Push   | Section |
   |              |                | Stream         | Stream |         |
   +==============+================+================+========+=========+
   | DATA         | No             | Yes            | Yes    | Section |
   |              |                |                |        | 7.2.1   |
   +--------------+----------------+----------------+--------+---------+
   | HEADERS      | No             | Yes            | Yes    | Section |
   |              |                |                |        | 7.2.2   |
   +--------------+----------------+----------------+--------+---------+
   | CANCEL_PUSH  | Yes            | No             | No     | Section |
   |              |                |                |        | 7.2.3   |
   +--------------+----------------+----------------+--------+---------+
   | SETTINGS     | Yes (1)        | No             | No     | Section |
   |              |                |                |        | 7.2.4   |
   +--------------+----------------+----------------+--------+---------+
   | PUSH_PROMISE | No             | Yes            | No     | Section |
   |              |                |                |        | 7.2.5   |
   +--------------+----------------+----------------+--------+---------+
   | GOAWAY       | Yes            | No             | No     | Section |
   |              |                |                |        | 7.2.6   |
   +--------------+----------------+----------------+--------+---------+
   | MAX_PUSH_ID  | Yes            | No             | No     | Section |
   |              |                |                |        | 7.2.7   |
   +--------------+----------------+----------------+--------+---------+
   | Reserved     | Yes            | Yes            | Yes    | Section |
   |              |                |                |        | 7.2.8   |
   +--------------+----------------+----------------+--------+---------+

              Table 1: HTTP/3 Frames and Stream Type Overview

   The SETTINGS frame can only occur as the first frame of a Control
   stream; this is indicated in Table 1 with a (1).  Specific guidance
   is provided in the relevant section.

   Note that, unlike QUIC frames, HTTP/3 frames can span multiple
   packets.

## 7.1  Frame Layout

   All frames have the following format:

   HTTP/3 Frame Format {
     Type (i),
     Length (i),
     Frame Payload (..),
   }

                       Figure 3: HTTP/3 Frame Format

   A frame includes the following fields:

   Type:  A variable-length integer that identifies the frame type.

   Length:  A variable-length integer that describes the length in bytes
      of the Frame Payload.

   Frame Payload:  A payload, the semantics of which are determined by
      the Type field.

> **MUST**: Each frame's payload MUST contain exactly the fields identified in
   its description.  A frame payload that contains additional bytes
   after the identified fields or a frame payload that terminates before
> **MUST**: the end of the identified fields MUST be treated as a connection
   error of type H3_FRAME_ERROR.  In particular, redundant length
> **MUST**: encodings MUST be verified to be self-consistent; see Section 10.8.

   When a stream terminates cleanly, if the last frame on the stream was
> **MUST**: truncated, this MUST be treated as a connection error of type
   H3_FRAME_ERROR.  Streams that terminate abruptly may be reset at any
   point in a frame.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes

- **`Http3FrameDecoder.cs`** — Parses the `Type (i) + Length (i) + Payload (..)` format using QUIC variable-length integer decoding; validates payload length matches declared length; raises `H3_FRAME_ERROR` for truncated frames or length mismatches per §7.1
- **`Http3FrameEncoder.cs`** — Encodes frames with variable-length integer Type and Length fields; all 7 defined frame types (DATA, HEADERS, CANCEL_PUSH, SETTINGS, PUSH_PROMISE, GOAWAY, MAX_PUSH_ID) use correct type codes
- **`QuicVariableLengthInteger.cs`** — Implements RFC 9000 §16 variable-length integer encoding/decoding used for frame Type and Length fields; validates self-consistency of redundant length encodings per §10.8

### Test References

- `TurboHttp.Tests/RFC9114/01_Http3FrameDecoderTests.cs` — Frame layout parsing, truncated frame detection, variable-length integer edge cases
- `TurboHttp.Tests/RFC9114/02_Http3FrameEncoderTests.cs` — Round-trip encoding/decoding for all frame types
- `TurboHttp.Tests/RFC9114/06_Http3FrameErrorTests.cs` — `H3_FRAME_ERROR` connection error tests for malformed frames

### Known Gaps

- None — frame layout parsing and validation is fully compliant with §7.1

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
