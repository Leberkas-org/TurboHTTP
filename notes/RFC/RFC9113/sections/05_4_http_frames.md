---
title: "4.  HTTP Frames"
rfc_number: 9113
rfc_section: "4"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 4: HTTP Frames — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, http_frames]
---

## 4.  HTTP Frames

4.  HTTP Frames

   Once the HTTP/2 connection is established, endpoints can begin
   exchanging frames.

## 4.1  Frame Format

   All frames begin with a fixed 9-octet header followed by a variable-
   length frame payload.

   HTTP Frame {
     Length (24),
     Type (8),

     Flags (8),

     Reserved (1),
     Stream Identifier (31),

     Frame Payload (..),
   }

                           Figure 1: Frame Layout

   The fields of the frame header are defined as:

   Length:  The length of the frame payload expressed as an unsigned
      24-bit integer in units of octets.  Values greater than 2^14
> **MUST NOT**: (16,384) MUST NOT be sent unless the receiver has set a larger
      value for SETTINGS_MAX_FRAME_SIZE.

      The 9 octets of the frame header are not included in this value.

   Type:  The 8-bit type of the frame.  The frame type determines the
      format and semantics of the frame.  Frames defined in this
> **MUST**: document are listed in Section 6.  Implementations MUST ignore and
      discard frames of unknown types.

   Flags:  An 8-bit field reserved for boolean flags specific to the
      frame type.

      Flags are assigned semantics specific to the indicated frame type.
      Unused flags are those that have no defined semantics for a
> **MUST**: particular frame type.  Unused flags MUST be ignored on receipt
   and MUST be left unset (0x00) when sending.

   Reserved:  A reserved 1-bit field.  The semantics of this bit are
> **MUST**: undefined, and the bit MUST remain unset (0x00) when sending and
   MUST be ignored when receiving.

   Stream Identifier:  A stream identifier (see Section 5.1.1) expressed
      as an unsigned 31-bit integer.  The value 0x00 is reserved for
      frames that are associated with the connection as a whole as
      opposed to an individual stream.

   The structure and content of the frame payload are dependent entirely
   on the frame type.

## 4.2  Frame Size

   The size of a frame payload is limited by the maximum size that a
   receiver advertises in the SETTINGS_MAX_FRAME_SIZE setting.  This
   setting can have any value between 2^14 (16,384) and 2^24-1
   (16,777,215) octets, inclusive.

> **MUST**: All implementations MUST be capable of receiving and minimally
   processing frames up to 2^14 octets in length, plus the 9-octet frame
   header (Section 4.1).  The size of the frame header is not included
   when describing frame sizes.

      |  Note: Certain frame types, such as PING (Section 6.7), impose
      |  additional limits on the amount of frame payload data allowed.

> **MUST**: An endpoint MUST send an error code of FRAME_SIZE_ERROR if a frame
   exceeds the size defined in SETTINGS_MAX_FRAME_SIZE, exceeds any
   limit defined for the frame type, or is too small to contain
   mandatory frame data.  A frame size error in a frame that could alter
> **MUST**: the state of the entire connection MUST be treated as a connection
   error (Section 5.4.1); this includes any frame carrying a field block
   (Section 4.3) (that is, HEADERS, PUSH_PROMISE, and CONTINUATION), a
   SETTINGS frame, and any frame with a stream identifier of 0.

   Endpoints are not obligated to use all available space in a frame.
   Responsiveness can be improved by using frames that are smaller than
   the permitted maximum size.  Sending large frames can result in
   delays in sending time-sensitive frames (such as RST_STREAM,
   WINDOW_UPDATE, or PRIORITY), which, if blocked by the transmission of
   a large frame, could affect performance.

## 4.3  Field Section Compression and Decompression

   Field section compression is the process of compressing a set of
   field lines (Section 5.2 of [HTTP]) to form a field block.  Field
   section decompression is the process of decoding a field block into a
   set of field lines.  Details of HTTP/2 field section compression and
   decompression are defined in [COMPRESSION], which, for historical
   reasons, refers to these processes as header compression and
   decompression.

   Each field block carries all of the compressed field lines of a
   single field section.  Header sections also include control data
   associated with the message in the form of pseudo-header fields
   (Section 8.3) that use the same format as a field line.

      |  Note: RFC 7540 [RFC7540] used the term "header block" in place
      |  of the more generic "field block".

   Field blocks carry control data and header sections for requests,
   responses, promised requests, and pushed responses (see Section 8.4).
   All these messages, except for interim responses and requests
   contained in PUSH_PROMISE (Section 6.6) frames, can optionally
   include a field block that carries a trailer section.

   A field section is a collection of field lines.  Each of the field
   lines in a field block carries a single value.  The serialized field
   block is then divided into one or more octet sequences, called field
   block fragments.  The first field block fragment is transmitted
   within the frame payload of HEADERS (Section 6.2) or PUSH_PROMISE
   (Section 6.6), each of which could be followed by CONTINUATION
   (Section 6.10) frames to carry subsequent field block fragments.

   The Cookie header field [COOKIE] is treated specially by the HTTP
   mapping (see Section 8.2.3).

   A receiving endpoint reassembles the field block by concatenating its
   fragments and then decompresses the block to reconstruct the field
   section.

   A complete field section consists of either:

   *  a single HEADERS or PUSH_PROMISE frame, with the END_HEADERS flag
      set, or

   *  a HEADERS or PUSH_PROMISE frame with the END_HEADERS flag unset
      and one or more CONTINUATION frames, where the last CONTINUATION
      frame has the END_HEADERS flag set.

> **MUST**: Each field block is processed as a discrete unit.  Field blocks MUST
   be transmitted as a contiguous sequence of frames, with no
   interleaved frames of any other type or from any other stream.  The
   last frame in a sequence of HEADERS or CONTINUATION frames has the
   END_HEADERS flag set.  The last frame in a sequence of PUSH_PROMISE
   or CONTINUATION frames has the END_HEADERS flag set.  This allows a
   field block to be logically equivalent to a single frame.

   Field block fragments can only be sent as the frame payload of
   HEADERS, PUSH_PROMISE, or CONTINUATION frames because these frames
   carry data that can modify the compression context maintained by a
   receiver.  An endpoint receiving HEADERS, PUSH_PROMISE, or
   CONTINUATION frames needs to reassemble field blocks and perform
   decompression even if the frames are to be discarded.  A receiver
> **MUST**: MUST terminate the connection with a connection error (Section 5.4.1)
   of type COMPRESSION_ERROR if it does not decompress a field block.

> **MUST**: A decoding error in a field block MUST be treated as a connection
   error (Section 5.4.1) of type COMPRESSION_ERROR.

### 4.3.1  Compression State

   Field compression is stateful.  Each endpoint has an HPACK encoder
   context and an HPACK decoder context that are used for encoding and
   decoding all field blocks on a connection.  Section 4 of
   [COMPRESSION] defines the dynamic table, which is the primary state
   for each context.

   The dynamic table has a maximum size that is set by an HPACK decoder.
   An endpoint communicates the size chosen by its HPACK decoder context
   using the SETTINGS_HEADER_TABLE_SIZE setting; see Section 6.5.2.
   When a connection is established, the dynamic table size for the
   HPACK decoder and encoder at both endpoints starts at 4,096 bytes,
   the initial value of the SETTINGS_HEADER_TABLE_SIZE setting.

   Any change to the maximum value set using SETTINGS_HEADER_TABLE_SIZE
   takes effect when the endpoint acknowledges settings (Section 6.5.3).
   The HPACK encoder at that endpoint can set the dynamic table to any
   size up to the maximum value set by the decoder.  An HPACK encoder
   declares the size of the dynamic table with a Dynamic Table Size
   Update instruction (Section 6.3 of [COMPRESSION]).

   Once an endpoint acknowledges a change to SETTINGS_HEADER_TABLE_SIZE
   that reduces the maximum below the current size of the dynamic table,
> **MUST**: its HPACK encoder MUST start the next field block with a Dynamic
   Table Size Update instruction that sets the dynamic table to a size
   that is less than or equal to the reduced maximum; see Section 4.2 of
> **MUST**: [COMPRESSION].  An endpoint MUST treat a field block that follows an
   acknowledgment of the reduction to the maximum dynamic table size as
   a connection error (Section 5.4.1) of type COMPRESSION_ERROR if it
   does not start with a conformant Dynamic Table Size Update
   instruction.

      |  Implementers are advised that reducing the value of
      |  SETTINGS_HEADER_TABLE_SIZE is not widely interoperable.  Use of
      |  the connection preface to reduce the value below the initial
      |  value of 4,096 is somewhat better supported, but this might
      |  fail with some implementations.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`Http2FrameDecoder.cs`** — Parses the 9-octet frame header per §4.1; validates SETTINGS_MAX_FRAME_SIZE limits per §4.2; raises FRAME_SIZE_ERROR for oversized frames
- **`Http2FrameEncoder.cs`** — Encodes all 10 defined frame types with correct type codes and flag handling
- **`HpackDecoder.cs`** — Full HPACK decompression per §4.3 with dynamic table state management
- **`HpackEncoder.cs`** — HPACK compression with static/dynamic table support

### Test References
- 482 total tests across 27 test files for RFC9113

### Known Gaps
- None

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
