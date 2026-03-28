---
title: "10.1.  Idle Timeout"
rfc_number: 9000
rfc_section: "10.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 10.1: Idle Timeout — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, idle_timeout]
---

# 10.1.  Idle Timeout



   An established QUIC connection can be terminated in one of three
   ways:

   *  idle timeout (Section 10.1)

   *  immediate close (Section 10.2)

   *  stateless reset (Section 10.3)

> **MAY**: An endpoint MAY discard connection state if it does not have a
   validated path on which it can send packets; see Section 8.2.

## 10.1.  Idle Timeout

   If a max_idle_timeout is specified by either endpoint in its
   transport parameters (Section 18.2), the connection is silently
   closed and its state is discarded when it remains idle for longer
   than the minimum of the max_idle_timeout value advertised by both
   endpoints.

   Each endpoint advertises a max_idle_timeout, but the effective value
   at an endpoint is computed as the minimum of the two advertised
   values (or the sole advertised value, if only one endpoint advertises
   a non-zero value).  By announcing a max_idle_timeout, an endpoint
   commits to initiating an immediate close (Section 10.2) if it
   abandons the connection prior to the effective value.

   An endpoint restarts its idle timer when a packet from its peer is
   received and processed successfully.  An endpoint also restarts its
   idle timer when sending an ack-eliciting packet if no other ack-
   eliciting packets have been sent since last receiving and processing
   a packet.  Restarting this timer when sending a packet ensures that
   connections are not closed after new activity is initiated.

> **MUST**: To avoid excessively small idle timeout periods, endpoints MUST
   increase the idle timeout period to be at least three times the
   current Probe Timeout (PTO).  This allows for multiple PTOs to
   expire, and therefore multiple probes to be sent and lost, prior to
   idle timeout.

### 10.1.1.  Liveness Testing

   An endpoint that sends packets close to the effective timeout risks
   having them be discarded at the peer, since the idle timeout period
   might have expired at the peer before these packets arrive.

   An endpoint can send a PING or another ack-eliciting frame to test
   the connection for liveness if the peer could time out soon, such as
   within a PTO; see Section 6.2 of [QUIC-RECOVERY].  This is especially
   useful if any available application data cannot be safely retried.
   Note that the application determines what data is safe to retry.

### 10.1.2.  Deferring Idle Timeout

   An endpoint might need to send ack-eliciting packets to avoid an idle
   timeout if it is expecting response data but does not have or is
   unable to send application data.

   An implementation of QUIC might provide applications with an option
   to defer an idle timeout.  This facility could be used when the
   application wishes to avoid losing state that has been associated
   with an open connection but does not expect to exchange application
   data for some time.  With this option, an endpoint could send a PING
   frame (Section 19.2) periodically, which will cause the peer to
   restart its idle timeout period.  Sending a packet containing a PING
   frame restarts the idle timeout for this endpoint also if this is the
   first ack-eliciting packet sent since receiving a packet.  Sending a
   PING frame causes the peer to respond with an acknowledgment, which
   also restarts the idle timeout for the endpoint.

> **SHOULD**: Application protocols that use QUIC SHOULD provide guidance on when
   deferring an idle timeout is appropriate.  Unnecessary sending of
   PING frames could have a detrimental effect on performance.

   A connection will time out if no packets are sent or received for a
   period longer than the time negotiated using the max_idle_timeout
   transport parameter; see Section 10.  However, state in middleboxes
   might time out earlier than that.  Though REQ-5 in [RFC4787]
   recommends a 2-minute timeout interval, experience shows that sending
   packets every 30 seconds is necessary to prevent the majority of
   middleboxes from losing state for UDP flows [GATEWAY].

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
