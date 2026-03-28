---
title: "Appendix A.  Pseudocode"
rfc_number: 9000
rfc_section: "Appendix A"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Appendix A: Pseudocode — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, pseudocode]
---

# Appendix A.  Pseudocode


   The pseudocode in this section describes sample algorithms.  These
   algorithms are intended to be correct and clear, rather than being
   optimally performant.

   The pseudocode segments in this section are licensed as Code
   Components; see the Copyright Notice.

A.1.  Sample Variable-Length Integer Decoding

   The pseudocode in Figure 45 shows how a variable-length integer can
   be read from a stream of bytes.  The function ReadVarint takes a
   single argument -- a sequence of bytes, which can be read in network
   byte order.

   ReadVarint(data):
     // The length of variable-length integers is encoded in the
     // first two bits of the first byte.

```abnf
     v = data.next_byte()
     prefix = v >> 6
     length = 1 << prefix
```


     // Once the length is known, remove these bits and read any
     // remaining bytes.

```abnf
     v = v & 0x3f
```

     repeat length-1 times:

```abnf
       v = (v << 8) + data.next_byte()
```

     return v

        Figure 45: Sample Variable-Length Integer Decoding Algorithm

   For example, the eight-byte sequence 0xc2197c5eff14e88c decodes to
   the decimal value 151,288,809,941,952,652; the four-byte sequence
   0x9d7f3e7d decodes to 494,878,333; the two-byte sequence 0x7bbd
   decodes to 15,293; and the single byte 0x25 decodes to 37 (as does
   the two-byte sequence 0x4025).

A.2.  Sample Packet Number Encoding Algorithm

   The pseudocode in Figure 46 shows how an implementation can select an
   appropriate size for packet number encodings.

   The EncodePacketNumber function takes two arguments:

   *  full_pn is the full packet number of the packet being sent.

   *  largest_acked is the largest packet number that has been
      acknowledged by the peer in the current packet number space, if
      any.

   EncodePacketNumber(full_pn, largest_acked):

     // The number of bits must be at least one more
     // than the base-2 logarithm of the number of contiguous
     // unacknowledged packet numbers, including the new packet.
     if largest_acked is None:
       num_unacked = full_pn + 1
     else:
       num_unacked = full_pn - largest_acked

     min_bits = log(num_unacked, 2) + 1
     num_bytes = ceil(min_bits / 8)

     // Encode the integer value and truncate to
     // the num_bytes least significant bytes.
     return encode(full_pn, num_bytes)

             Figure 46: Sample Packet Number Encoding Algorithm

   For example, if an endpoint has received an acknowledgment for packet
   0xabe8b3 and is sending a packet with a number of 0xac5c02, there are
   29,519 (0x734f) outstanding packet numbers.  In order to represent at
   least twice this range (59,038 packets, or 0xe69e), 16 bits are
   required.

   In the same state, sending a packet with a number of 0xace8fe uses
   the 24-bit encoding, because at least 18 bits are required to
   represent twice the range (131,222 packets, or 0x020096).

A.3.  Sample Packet Number Decoding Algorithm

   The pseudocode in Figure 47 includes an example algorithm for
   decoding packet numbers after header protection has been removed.

   The DecodePacketNumber function takes three arguments:

   *  largest_pn is the largest packet number that has been successfully
      processed in the current packet number space.

   *  truncated_pn is the value of the Packet Number field.

   *  pn_nbits is the number of bits in the Packet Number field (8, 16,
      24, or 32).

   DecodePacketNumber(largest_pn, truncated_pn, pn_nbits):
      expected_pn  = largest_pn + 1
      pn_win       = 1 << pn_nbits
      pn_hwin      = pn_win / 2
      pn_mask      = pn_win - 1
      // The incoming packet number should be greater than
      // expected_pn - pn_hwin and less than or equal to
      // expected_pn + pn_hwin
      //
      // This means we cannot just strip the trailing bits from
      // expected_pn and add the truncated_pn because that might
      // yield a value outside the window.
      //
      // The following code calculates a candidate value and
      // makes sure it's within the packet number window.
      // Note the extra checks to prevent overflow and underflow.
      candidate_pn = (expected_pn & ~pn_mask) | truncated_pn
      if candidate_pn <= expected_pn - pn_hwin and
         candidate_pn < (1 << 62) - pn_win:
         return candidate_pn + pn_win
      if candidate_pn > expected_pn + pn_hwin and
         candidate_pn >= pn_win:
         return candidate_pn - pn_win
      return candidate_pn

             Figure 47: Sample Packet Number Decoding Algorithm

   For example, if the highest successfully authenticated packet had a
   packet number of 0xa82f30ea, then a packet containing a 16-bit value
   of 0x9b32 will be decoded as 0xa82f9b32.

A.4.  Sample ECN Validation Algorithm

   Each time an endpoint commences sending on a new network path, it
   determines whether the path supports ECN; see Section 13.4.  If the
   path supports ECN, the goal is to use ECN.  Endpoints might also
   periodically reassess a path that was determined to not support ECN.

   This section describes one method for testing new paths.  This
   algorithm is intended to show how a path might be tested for ECN
   support.  Endpoints can implement different methods.

   The path is assigned an ECN state that is one of "testing",
   "unknown", "failed", or "capable".  On paths with a "testing" or
   "capable" state, the endpoint sends packets with an ECT marking --
   ECT(0) by default; otherwise, the endpoint sends unmarked packets.

   To start testing a path, the ECN state is set to "testing", and
   existing ECN counts are remembered as a baseline.

   The testing period runs for a number of packets or a limited time, as
   determined by the endpoint.  The goal is not to limit the duration of
   the testing period but to ensure that enough marked packets are sent
   for received ECN counts to provide a clear indication of how the path
   treats marked packets.  Section 13.4.2 suggests limiting this to ten
   packets or three times the PTO.

   After the testing period ends, the ECN state for the path becomes
   "unknown".  From the "unknown" state, successful validation of the
   ECN counts in an ACK frame (see Section 13.4.2.1) causes the ECN
   state for the path to become "capable", unless no marked packet has
   been acknowledged.

   If validation of ECN counts fails at any time, the ECN state for the
   affected path becomes "failed".  An endpoint can also mark the ECN
   state for a path as "failed" if marked packets are all declared lost
   or if they are all ECN-CE marked.

   Following this algorithm ensures that ECN is rarely disabled for
   paths that properly support ECN.  Any path that incorrectly modifies
   markings will cause ECN to be disabled.  For those rare cases where
   marked packets are discarded by the path, the short duration of the
   testing period limits the number of losses incurred.

Contributors

   The original design and rationale behind this protocol draw
   significantly from work by Jim Roskind [EARLY-DESIGN].

   The IETF QUIC Working Group received an enormous amount of support
   from many people.  The following people provided substantive
   contributions to this document:

   *  Alessandro Ghedini
   *  Alyssa Wilk
   *  Antoine Delignat-Lavaud
   *  Brian Trammell
   *  Christian Huitema
   *  Colin Perkins
   *  David Schinazi
   *  Dmitri Tikhonov
   *  Eric Kinnear
   *  Eric Rescorla
   *  Gorry Fairhurst
   *  Ian Swett
   *  Igor Lubashev
   *  奥 一穂 (Kazuho Oku)
   *  Lars Eggert
   *  Lucas Pardue
   *  Magnus Westerlund
   *  Marten Seemann
   *  Martin Duke
   *  Mike Bishop
   *  Mikkel Fahnøe Jørgensen
   *  Mirja Kühlewind
   *  Nick Banks
   *  Nick Harper
   *  Patrick McManus
   *  Roberto Peon
   *  Ryan Hamilton
   *  Subodh Iyengar
   *  Tatsuhiro Tsujikawa
   *  Ted Hardie
   *  Tom Jones
   *  Victor Vasiliev

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
