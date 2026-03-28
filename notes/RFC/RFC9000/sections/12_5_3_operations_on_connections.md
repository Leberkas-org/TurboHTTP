---
title: "5.3.  Operations on Connections"
rfc_number: 9000
rfc_section: "5.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 5.3: Operations on Connections — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, operations_on_connections]
---

# 5.3.  Operations on Connections


   This document does not define an API for QUIC; it instead defines a
   set of functions for QUIC connections that application protocols can
   rely upon.  An application protocol can assume that an implementation
   of QUIC provides an interface that includes the operations described
   in this section.  An implementation designed for use with a specific
   application protocol might provide only those operations that are
   used by that protocol.

   When implementing the client role, an application protocol can:

   *  open a connection, which begins the exchange described in
      Section 7;

   *  enable Early Data when available; and

   *  be informed when Early Data has been accepted or rejected by a
      server.

   When implementing the server role, an application protocol can:

   *  listen for incoming connections, which prepares for the exchange
      described in Section 7;

   *  if Early Data is supported, embed application-controlled data in
      the TLS resumption ticket sent to the client; and

   *  if Early Data is supported, retrieve application-controlled data
      from the client's resumption ticket and accept or reject Early
      Data based on that information.

   In either role, an application protocol can:

   *  configure minimum values for the initial number of permitted
      streams of each type, as communicated in the transport parameters
      (Section 7.4);

   *  control resource allocation for receive buffers by setting flow
      control limits both for streams and for the connection;

   *  identify whether the handshake has completed successfully or is
      still ongoing;

   *  keep a connection from silently closing, by either generating PING
      frames (Section 19.2) or requesting that the transport send
      additional frames before the idle timeout expires (Section 10.1);
      and

   *  immediately close (Section 10.2) the connection.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
