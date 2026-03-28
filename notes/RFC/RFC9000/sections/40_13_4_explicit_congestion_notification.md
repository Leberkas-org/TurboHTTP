---
title: "13.4.  Explicit Congestion Notification"
rfc_number: 9000
rfc_section: "13.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 13.4: Explicit Congestion Notification — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, explicit_congestion_notification]
---

# 13.4.  Explicit Congestion Notification


   QUIC endpoints can use ECN [RFC3168] to detect and respond to network
   congestion.  ECN allows an endpoint to set an ECN-Capable Transport
   (ECT) codepoint in the ECN field of an IP packet.  A network node can
   then indicate congestion by setting the ECN-CE codepoint in the ECN
   field instead of dropping the packet [RFC8087].  Endpoints react to
   reported congestion by reducing their sending rate in response, as
   described in [QUIC-RECOVERY].

   To enable ECN, a sending QUIC endpoint first determines whether a
   path supports ECN marking and whether the peer reports the ECN values
   in received IP headers; see Section 13.4.2.

### 13.4.1.  Reporting ECN Counts

   The use of ECN requires the receiving endpoint to read the ECN field
   from an IP packet, which is not possible on all platforms.  If an
   endpoint does not implement ECN support or does not have access to
   received ECN fields, it does not report ECN counts for packets it
   receives.

   Even if an endpoint does not set an ECT field in packets it sends,
> **MUST**: the endpoint MUST provide feedback about ECN markings it receives, if
   these are accessible.  Failing to report the ECN counts will cause
   the sender to disable the use of ECN for this connection.

   On receiving an IP packet with an ECT(0), ECT(1), or ECN-CE
   codepoint, an ECN-enabled endpoint accesses the ECN field and
   increases the corresponding ECT(0), ECT(1), or ECN-CE count.  These
   ECN counts are included in subsequent ACK frames; see Sections 13.2
   and 19.3.

   Each packet number space maintains separate acknowledgment state and
   separate ECN counts.  Coalesced QUIC packets (see Section 12.2) share
   the same IP header so the ECN counts are incremented once for each
   coalesced QUIC packet.

   For example, if one each of an Initial, Handshake, and 1-RTT QUIC
   packet are coalesced into a single UDP datagram, the ECN counts for
   all three packet number spaces will be incremented by one each, based
   on the ECN field of the single IP header.

   ECN counts are only incremented when QUIC packets from the received
   IP packet are processed.  As such, duplicate QUIC packets are not
   processed and do not increase ECN counts; see Section 21.10 for
   relevant security concerns.

### 13.4.2.  ECN Validation

   It is possible for faulty network devices to corrupt or erroneously
   drop packets that carry a non-zero ECN codepoint.  To ensure
   connectivity in the presence of such devices, an endpoint validates
   the ECN counts for each network path and disables the use of ECN on
   that path if errors are detected.

   To perform ECN validation for a new path:

   *  The endpoint sets an ECT(0) codepoint in the IP header of early
      outgoing packets sent on a new path to the peer [RFC8311].

   *  The endpoint monitors whether all packets sent with an ECT
      codepoint are eventually deemed lost (Section 6 of
      [QUIC-RECOVERY]), indicating that ECN validation has failed.

   If an endpoint has cause to expect that IP packets with an ECT
   codepoint might be dropped by a faulty network element, the endpoint
   could set an ECT codepoint for only the first ten outgoing packets on
   a path, or for a period of three PTOs (see Section 6.2 of
   [QUIC-RECOVERY]).  If all packets marked with non-zero ECN codepoints
   are subsequently lost, it can disable marking on the assumption that
   the marking caused the loss.

   An endpoint thus attempts to use ECN and validates this for each new
   connection, when switching to a server's preferred address, and on
   active connection migration to a new path.  Appendix A.4 describes
   one possible algorithm.

   Other methods of probing paths for ECN support are possible, as are
> **MAY**: different marking strategies.  Implementations MAY use other methods
   defined in RFCs; see [RFC8311].  Implementations that use the ECT(1)
   codepoint need to perform ECN validation using the reported ECT(1)
   counts.

### 13.4.2.1.  Receiving ACK Frames with ECN Counts

   Erroneous application of ECN-CE markings by the network can result in
   degraded connection performance.  An endpoint that receives an ACK
   frame with ECN counts therefore validates the counts before using
   them.  It performs this validation by comparing newly received counts
   against those from the last successfully processed ACK frame.  Any
   increase in the ECN counts is validated based on the ECN markings
   that were applied to packets that are newly acknowledged in the ACK
   frame.

   If an ACK frame newly acknowledges a packet that the endpoint sent
   with either the ECT(0) or ECT(1) codepoint set, ECN validation fails
   if the corresponding ECN counts are not present in the ACK frame.
   This check detects a network element that zeroes the ECN field or a
   peer that does not report ECN markings.

   ECN validation also fails if the sum of the increase in ECT(0) and
   ECN-CE counts is less than the number of newly acknowledged packets
   that were originally sent with an ECT(0) marking.  Similarly, ECN
   validation fails if the sum of the increases to ECT(1) and ECN-CE
   counts is less than the number of newly acknowledged packets sent
   with an ECT(1) marking.  These checks can detect remarking of ECN-CE
   markings by the network.

   An endpoint could miss acknowledgments for a packet when ACK frames
   are lost.  It is therefore possible for the total increase in ECT(0),
   ECT(1), and ECN-CE counts to be greater than the number of packets
   that are newly acknowledged by an ACK frame.  This is why ECN counts
   are permitted to be larger than the total number of packets that are
   acknowledged.

   Validating ECN counts from reordered ACK frames can result in
> **MUST NOT**: failure.  An endpoint MUST NOT fail ECN validation as a result of
   processing an ACK frame that does not increase the largest
   acknowledged packet number.

   ECN validation can fail if the received total count for either ECT(0)
   or ECT(1) exceeds the total number of packets sent with each
   corresponding ECT codepoint.  In particular, validation will fail
   when an endpoint receives a non-zero ECN count corresponding to an
   ECT codepoint that it never applied.  This check detects when packets
   are remarked to ECT(0) or ECT(1) in the network.

### 13.4.2.2.  ECN Validation Outcomes

> **MUST**: If validation fails, then the endpoint MUST disable ECN.  It stops
   setting the ECT codepoint in IP packets that it sends, assuming that
   either the network path or the peer does not support ECN.

> **MAY**: Even if validation fails, an endpoint MAY revalidate ECN for the same
   path at any later time in the connection.  An endpoint could continue
   to periodically attempt validation.

> **MAY**: Upon successful validation, an endpoint MAY continue to set an ECT
   codepoint in subsequent packets it sends, with the expectation that
   the path is ECN capable.  Network routing and path elements can
> **MUST**: change mid-connection; an endpoint MUST disable ECN if validation
   later fails.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
