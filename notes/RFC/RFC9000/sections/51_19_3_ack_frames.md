---
title: "19.3.  ACK Frames"
rfc_number: 9000
rfc_section: "19.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.3: ACK Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, ack_frames]
---

# 19.3.  ACK Frames


   Receivers send ACK frames (types 0x02 and 0x03) to inform senders of
   packets they have received and processed.  The ACK frame contains one
   or more ACK Ranges.  ACK Ranges identify acknowledged packets.  If
   the frame type is 0x03, ACK frames also contain the cumulative count
   of QUIC packets with associated ECN marks received on the connection
> **MUST**: up until this point.  QUIC implementations MUST properly handle both
   types, and, if they have enabled ECN for packets they send, they
> **SHOULD**: SHOULD use the information in the ECN section to manage their
   congestion state.

   QUIC acknowledgments are irrevocable.  Once acknowledged, a packet
   remains acknowledged, even if it does not appear in a future ACK
   frame.  This is unlike reneging for TCP Selective Acknowledgments
   (SACKs) [RFC2018].

   Packets from different packet number spaces can be identified using
   the same numeric value.  An acknowledgment for a packet needs to
   indicate both a packet number and a packet number space.  This is
   accomplished by having each ACK frame only acknowledge packet numbers
   in the same space as the packet in which the ACK frame is contained.

   Version Negotiation and Retry packets cannot be acknowledged because
   they do not contain a packet number.  Rather than relying on ACK
   frames, these packets are implicitly acknowledged by the next Initial
   packet sent by the client.

   ACK frames are formatted as shown in Figure 25.

   ACK Frame {
     Type (i) = 0x02..0x03,
     Largest Acknowledged (i),
     ACK Delay (i),
     ACK Range Count (i),
     First ACK Range (i),
     ACK Range (..) ...,
     [ECN Counts (..)],
   }

                        Figure 25: ACK Frame Format

   ACK frames contain the following fields:

   Largest Acknowledged:  A variable-length integer representing the
      largest packet number the peer is acknowledging; this is usually
      the largest packet number that the peer has received prior to
      generating the ACK frame.  Unlike the packet number in the QUIC
      long or short header, the value in an ACK frame is not truncated.

   ACK Delay:  A variable-length integer encoding the acknowledgment
      delay in microseconds; see Section 13.2.5.  It is decoded by
      multiplying the value in the field by 2 to the power of the
      ack_delay_exponent transport parameter sent by the sender of the
      ACK frame; see Section 18.2.  Compared to simply expressing the
      delay as an integer, this encoding allows for a larger range of
      values within the same number of bytes, at the cost of lower
      resolution.

   ACK Range Count:  A variable-length integer specifying the number of
      ACK Range fields in the frame.

   First ACK Range:  A variable-length integer indicating the number of
      contiguous packets preceding the Largest Acknowledged that are
      being acknowledged.  That is, the smallest packet acknowledged in
      the range is determined by subtracting the First ACK Range value
      from the Largest Acknowledged field.

   ACK Ranges:  Contains additional ranges of packets that are
      alternately not acknowledged (Gap) and acknowledged (ACK Range);
      see Section 19.3.1.

   ECN Counts:  The three ECN counts; see Section 19.3.2.

### 19.3.1.  ACK Ranges

   Each ACK Range consists of alternating Gap and ACK Range Length
   values in descending packet number order.  ACK Ranges can be
   repeated.  The number of Gap and ACK Range Length values is
   determined by the ACK Range Count field; one of each value is present
   for each value in the ACK Range Count field.

   ACK Ranges are structured as shown in Figure 26.

   ACK Range {
     Gap (i),
     ACK Range Length (i),
   }

                           Figure 26: ACK Ranges

   The fields that form each ACK Range are:

   Gap:  A variable-length integer indicating the number of contiguous
      unacknowledged packets preceding the packet number one lower than
      the smallest in the preceding ACK Range.

   ACK Range Length:  A variable-length integer indicating the number of
      contiguous acknowledged packets preceding the largest packet
      number, as determined by the preceding Gap.

   Gap and ACK Range Length values use a relative integer encoding for
   efficiency.  Though each encoded value is positive, the values are
   subtracted, so that each ACK Range describes progressively lower-
   numbered packets.

   Each ACK Range acknowledges a contiguous range of packets by
   indicating the number of acknowledged packets that precede the
   largest packet number in that range.  A value of 0 indicates that
   only the largest packet number is acknowledged.  Larger ACK Range
   values indicate a larger range, with corresponding lower values for
   the smallest packet number in the range.  Thus, given a largest
   packet number for the range, the smallest value is determined by the
   following formula:


```abnf
      smallest = largest - ack_range
```


   An ACK Range acknowledges all packets between the smallest packet
   number and the largest, inclusive.

   The largest value for an ACK Range is determined by cumulatively
   subtracting the size of all preceding ACK Range Lengths and Gaps.

   Each Gap indicates a range of packets that are not being
   acknowledged.  The number of packets in the gap is one higher than
   the encoded value of the Gap field.

   The value of the Gap field establishes the largest packet number
   value for the subsequent ACK Range using the following formula:


```abnf
      largest = previous_smallest - gap - 2
```


> **MUST**: If any computed packet number is negative, an endpoint MUST generate
   a connection error of type FRAME_ENCODING_ERROR.

### 19.3.2.  ECN Counts

   The ACK frame uses the least significant bit of the type value (that
   is, type 0x03) to indicate ECN feedback and report receipt of QUIC
   packets with associated ECN codepoints of ECT(0), ECT(1), or ECN-CE
   in the packet's IP header.  ECN counts are only present when the ACK
   frame type is 0x03.

   When present, there are three ECN counts, as shown in Figure 27.

   ECN Counts {
     ECT0 Count (i),
     ECT1 Count (i),
     ECN-CE Count (i),
   }

                        Figure 27: ECN Count Format

   The ECN count fields are:

   ECT0 Count:  A variable-length integer representing the total number
      of packets received with the ECT(0) codepoint in the packet number
      space of the ACK frame.

   ECT1 Count:  A variable-length integer representing the total number
      of packets received with the ECT(1) codepoint in the packet number
      space of the ACK frame.

   ECN-CE Count:  A variable-length integer representing the total
      number of packets received with the ECN-CE codepoint in the packet
      number space of the ACK frame.

   ECN counts are maintained separately for each packet number space.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
