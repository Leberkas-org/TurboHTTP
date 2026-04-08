---
title: "5.2.  Flow Control"
rfc_number: 9113
rfc_section: "5.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 5.2: Flow Control — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, flow_control]
---

## 5.2.  Flow Control

## 5.2  Flow Control

   Using streams for multiplexing introduces contention over use of the
   TCP connection, resulting in blocked streams.  A flow-control scheme
   ensures that streams on the same connection do not destructively
   interfere with each other.  Flow control is used for both individual
   streams and the connection as a whole.

   HTTP/2 provides for flow control through use of the WINDOW_UPDATE
   frame (Section 6.9).

### 5.2.1  Flow-Control Principles

   HTTP/2 stream flow control aims to allow a variety of flow-control
   algorithms to be used without requiring protocol changes.  Flow
   control in HTTP/2 has the following characteristics:

   1.  Flow control is specific to a connection.  HTTP/2 flow control
       operates between the endpoints of a single hop and not over the
       entire end-to-end path.

   2.  Flow control is based on WINDOW_UPDATE frames.  Receivers
       advertise how many octets they are prepared to receive on a
       stream and for the entire connection.  This is a credit-based
       scheme.

   3.  Flow control is directional with overall control provided by the
> **MAY**: receiver.  A receiver MAY choose to set any window size that it
       desires for each stream and for the entire connection.  A sender
> **MUST**: MUST respect flow-control limits imposed by a receiver.  Clients,
       servers, and intermediaries all independently advertise their
       flow-control window as a receiver and abide by the flow-control
       limits set by their peer when sending.

   4.  The initial value for the flow-control window is 65,535 octets
       for both new streams and the overall connection.

   5.  The frame type determines whether flow control applies to a
       frame.  Of the frames specified in this document, only DATA
       frames are subject to flow control; all other frame types do not
       consume space in the advertised flow-control window.  This
       ensures that important control frames are not blocked by flow
       control.

   6.  An endpoint can choose to disable its own flow control, but an
       endpoint cannot ignore flow-control signals from its peer.

   7.  HTTP/2 defines only the format and semantics of the WINDOW_UPDATE
       frame (Section 6.9).  This document does not stipulate how a
       receiver decides when to send this frame or the value that it
       sends, nor does it specify how a sender chooses to send packets.
       Implementations are able to select any algorithm that suits their
       needs.

   Implementations are also responsible for prioritizing the sending of
   requests and responses, choosing how to avoid head-of-line blocking
   for requests, and managing the creation of new streams.  Algorithm
   choices for these could interact with any flow-control algorithm.

### 5.2.2  Appropriate Use of Flow Control

   Flow control is defined to protect endpoints that are operating under
   resource constraints.  For example, a proxy needs to share memory
   between many connections and also might have a slow upstream
   connection and a fast downstream one.  Flow control addresses cases
   where the receiver is unable to process data on one stream yet wants
   to continue to process other streams in the same connection.

   Deployments that do not require this capability can advertise a flow-
   control window of the maximum size (2^31-1) and can maintain this
   window by sending a WINDOW_UPDATE frame when any data is received.
   This effectively disables flow control for that receiver.
   Conversely, a sender is always subject to the flow-control window
   advertised by the receiver.

   Deployments with constrained resources (for example, memory) can
   employ flow control to limit the amount of memory a peer can consume.
   Note, however, that this can lead to suboptimal use of available
   network resources if flow control is enabled without knowledge of the
   bandwidth * delay product (see [RFC7323]).

   Even with full awareness of the current bandwidth * delay product,
> **MUST**: implementation of flow control can be difficult.  Endpoints MUST read
   and process HTTP/2 frames from the TCP receive buffer as soon as data
   is available.  Failure to read promptly could lead to a deadlock when
   critical frames, such as WINDOW_UPDATE, are not read and acted upon.
   Reading frames promptly does not expose endpoints to resource
   exhaustion attacks, as HTTP/2 flow control limits resource
   commitments.

### 5.2.3  Flow-Control Performance

   If an endpoint cannot ensure that its peer always has available flow-
   control window space that is greater than the peer's bandwidth *
   delay product on this connection, its receive throughput will be
   limited by HTTP/2 flow control.  This will result in degraded
   performance.

   Sending timely WINDOW_UPDATE frames can improve performance.
   Endpoints will want to balance the need to improve receive throughput
   with the need to manage resource exhaustion risks and should take
   careful note of Section 10.5 in defining their strategy to manage
   window sizes.

---

## TurboHTTP Compliance

**Status**: ✅ Compliant

### Implementation Notes

- **`Http2FlowController.cs`** — Implements credit-based flow control per §5.2.1; tracks both stream-level and connection-level windows; initial window size 65,535 octets per §5.2.1 principle 4; only DATA frames consume flow-control credit per principle 5
- **`Http2WindowUpdateHandler.cs`** — Processes WINDOW_UPDATE frames to increase flow-control windows; raises `FLOW_CONTROL_ERROR` when window exceeds 2^31-1
- **`Http2Connection.cs`** — Reads and processes frames from TCP buffer promptly per §5.2.2 to prevent deadlock on WINDOW_UPDATE frames

### Test References

- `TurboHTTP.Tests/RFC9113/08_Http2FlowControlTests.cs` — Window tracking, credit consumption, overflow detection
- `TurboHTTP.Tests/RFC9113/09_Http2WindowUpdateTests.cs` — WINDOW_UPDATE processing, connection vs stream windows
- `TurboHTTP.StreamTests/` — End-to-end flow control under backpressure

### Known Gaps

- ⚠️ Adaptive window sizing — uses fixed window management rather than bandwidth*delay product-aware algorithm per §5.2.3; functional but may not achieve optimal throughput on high-latency connections

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
