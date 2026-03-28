---
title: "7.4.  Transport Parameters"
rfc_number: 9000
rfc_section: "7.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 7.4: Transport Parameters — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, transport_parameters]
---

# 7.4.  Transport Parameters


   During connection establishment, both endpoints make authenticated
   declarations of their transport parameters.  Endpoints are required
   to comply with the restrictions that each parameter defines; the
   description of each parameter includes rules for its handling.

   Transport parameters are declarations that are made unilaterally by
   each endpoint.  Each endpoint can choose values for transport
   parameters independent of the values chosen by its peer.

   The encoding of the transport parameters is detailed in Section 18.

   QUIC includes the encoded transport parameters in the cryptographic
   handshake.  Once the handshake completes, the transport parameters
   declared by the peer are available.  Each endpoint validates the
   values provided by its peer.

   Definitions for each of the defined transport parameters are included
   in Section 18.2.

> **MUST**: An endpoint MUST treat receipt of a transport parameter with an
   invalid value as a connection error of type
   TRANSPORT_PARAMETER_ERROR.

> **MUST NOT**: An endpoint MUST NOT send a parameter more than once in a given
   transport parameters extension.  An endpoint SHOULD treat receipt of
   duplicate transport parameters as a connection error of type
   TRANSPORT_PARAMETER_ERROR.

   Endpoints use transport parameters to authenticate the negotiation of
   connection IDs during the handshake; see Section 7.3.

   ALPN (see [ALPN]) allows clients to offer multiple application
   protocols during connection establishment.  The transport parameters
   that a client includes during the handshake apply to all application
   protocols that the client offers.  Application protocols can
   recommend values for transport parameters, such as the initial flow
   control limits.  However, application protocols that set constraints
   on values for transport parameters could make it impossible for a
   client to offer multiple application protocols if these constraints
   conflict.

### 7.4.1.  Values of Transport Parameters for 0-RTT

   Using 0-RTT depends on both client and server using protocol
   parameters that were negotiated from a previous connection.  To
   enable 0-RTT, endpoints store the values of the server transport
   parameters with any session tickets it receives on the connection.
   Endpoints also store any information required by the application
   protocol or cryptographic handshake; see Section 4.6 of [QUIC-TLS].
   The values of stored transport parameters are used when attempting
   0-RTT using the session tickets.

   Remembered transport parameters apply to the new connection until the
   handshake completes and the client starts sending 1-RTT packets.
   Once the handshake completes, the client uses the transport
   parameters established in the handshake.  Not all transport
   parameters are remembered, as some do not apply to future connections
   or they have no effect on the use of 0-RTT.

> **MUST**: The definition of a new transport parameter (Section 7.4.2) MUST
   specify whether storing the transport parameter for 0-RTT is
   mandatory, optional, or prohibited.  A client need not store a
   transport parameter it cannot process.

> **MUST NOT**: A client MUST NOT use remembered values for the following parameters:
   ack_delay_exponent, max_ack_delay, initial_source_connection_id,
   original_destination_connection_id, preferred_address,
   retry_source_connection_id, and stateless_reset_token.  The client
> **MUST**: MUST use the server's new values in the handshake instead; if the
   server does not provide new values, the default values are used.

> **MUST**: A client that attempts to send 0-RTT data MUST remember all other
   transport parameters used by the server that it is able to process.
   The server can remember these transport parameters or can store an
   integrity-protected copy of the values in the ticket and recover the
   information when accepting 0-RTT data.  A server uses the transport
   parameters in determining whether to accept 0-RTT data.

> **MUST NOT**: If 0-RTT data is accepted by the server, the server MUST NOT reduce
   any limits or alter any values that might be violated by the client
   with its 0-RTT data.  In particular, a server that accepts 0-RTT data
> **MUST NOT**: MUST NOT set values for the following parameters (Section 18.2) that
   are smaller than the remembered values of the parameters.

   *  active_connection_id_limit

   *  initial_max_data

   *  initial_max_stream_data_bidi_local

   *  initial_max_stream_data_bidi_remote

   *  initial_max_stream_data_uni

   *  initial_max_streams_bidi

   *  initial_max_streams_uni

   Omitting or setting a zero value for certain transport parameters can
   result in 0-RTT data being enabled but not usable.  The applicable
   subset of transport parameters that permit the sending of application
> **SHOULD**: data SHOULD be set to non-zero values for 0-RTT.  This includes
   initial_max_data and either (1) initial_max_streams_bidi and
   initial_max_stream_data_bidi_remote or (2) initial_max_streams_uni
   and initial_max_stream_data_uni.

   A server might provide larger initial stream flow control limits for
   streams than the remembered values that a client applies when sending
   0-RTT.  Once the handshake completes, the client updates the flow
   control limits on all sending streams using the updated values of
   initial_max_stream_data_bidi_remote and initial_max_stream_data_uni.

> **MAY**: A server MAY store and recover the previously sent values of the
   max_idle_timeout, max_udp_payload_size, and disable_active_migration
   parameters and reject 0-RTT if it selects smaller values.  Lowering
   the values of these parameters while also accepting 0-RTT data could
   degrade the performance of the connection.  Specifically, lowering
   the max_udp_payload_size could result in dropped packets, leading to
   worse performance compared to rejecting 0-RTT data outright.

> **MUST**: A server MUST reject 0-RTT data if the restored values for transport
   parameters cannot be supported.

> **MUST**: When sending frames in 0-RTT packets, a client MUST only use
   remembered transport parameters; importantly, it MUST NOT use updated
   values that it learns from the server's updated transport parameters
   or from frames received in 1-RTT packets.  Updated values of
   transport parameters from the handshake apply only to 1-RTT packets.
   For instance, flow control limits from remembered transport
   parameters apply to all 0-RTT packets even if those values are
   increased by the handshake or by frames sent in 1-RTT packets.  A
> **MAY**: server MAY treat the use of updated transport parameters in 0-RTT as
   a connection error of type PROTOCOL_VIOLATION.

### 7.4.2.  New Transport Parameters

   New transport parameters can be used to negotiate new protocol
> **MUST**: behavior.  An endpoint MUST ignore transport parameters that it does
   not support.  The absence of a transport parameter therefore disables
   any optional protocol feature that is negotiated using the parameter.
   As described in Section 18.1, some identifiers are reserved in order
   to exercise this requirement.

   A client that does not understand a transport parameter can discard
   it and attempt 0-RTT on subsequent connections.  However, if the
   client adds support for a discarded transport parameter, it risks
   violating the constraints that the transport parameter establishes if
   it attempts 0-RTT.  New transport parameters can avoid this problem
   by setting a default of the most conservative value.  Clients can
   avoid this problem by remembering all parameters, even those not
   currently supported.

   New transport parameters can be registered according to the rules in
   Section 22.3.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
