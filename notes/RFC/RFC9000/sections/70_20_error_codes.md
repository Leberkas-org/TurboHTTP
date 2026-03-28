---
title: "20.  Error Codes"
rfc_number: 9000
rfc_section: "20"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 20: Error Codes — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, error_codes]
---

# 20.  Error Codes


   QUIC transport error codes and application error codes are 62-bit
   unsigned integers.

## 20.1.  Transport Error Codes

   This section lists the defined QUIC transport error codes that can be
   used in a CONNECTION_CLOSE frame with a type of 0x1c.  These errors
   apply to the entire connection.

   NO_ERROR (0x00):  An endpoint uses this with CONNECTION_CLOSE to
      signal that the connection is being closed abruptly in the absence
      of any error.

   INTERNAL_ERROR (0x01):  The endpoint encountered an internal error
      and cannot continue with the connection.

   CONNECTION_REFUSED (0x02):  The server refused to accept a new
      connection.

   FLOW_CONTROL_ERROR (0x03):  An endpoint received more data than it
      permitted in its advertised data limits; see Section 4.

   STREAM_LIMIT_ERROR (0x04):  An endpoint received a frame for a stream
      identifier that exceeded its advertised stream limit for the
      corresponding stream type.

   STREAM_STATE_ERROR (0x05):  An endpoint received a frame for a stream
      that was not in a state that permitted that frame; see Section 3.

   FINAL_SIZE_ERROR (0x06):  (1) An endpoint received a STREAM frame
      containing data that exceeded the previously established final
      size, (2) an endpoint received a STREAM frame or a RESET_STREAM
      frame containing a final size that was lower than the size of
      stream data that was already received, or (3) an endpoint received
      a STREAM frame or a RESET_STREAM frame containing a different
      final size to the one already established.

   FRAME_ENCODING_ERROR (0x07):  An endpoint received a frame that was
      badly formatted -- for instance, a frame of an unknown type or an
      ACK frame that has more acknowledgment ranges than the remainder
      of the packet could carry.

   TRANSPORT_PARAMETER_ERROR (0x08):  An endpoint received transport
      parameters that were badly formatted, included an invalid value,
      omitted a mandatory transport parameter, included a forbidden
      transport parameter, or were otherwise in error.

   CONNECTION_ID_LIMIT_ERROR (0x09):  The number of connection IDs
      provided by the peer exceeds the advertised
      active_connection_id_limit.

   PROTOCOL_VIOLATION (0x0a):  An endpoint detected an error with
      protocol compliance that was not covered by more specific error
      codes.

   INVALID_TOKEN (0x0b):  A server received a client Initial that
      contained an invalid Token field.

   APPLICATION_ERROR (0x0c):  The application or application protocol
      caused the connection to be closed.

   CRYPTO_BUFFER_EXCEEDED (0x0d):  An endpoint has received more data in
      CRYPTO frames than it can buffer.

   KEY_UPDATE_ERROR (0x0e):  An endpoint detected errors in performing
      key updates; see Section 6 of [QUIC-TLS].

   AEAD_LIMIT_REACHED (0x0f):  An endpoint has reached the
      confidentiality or integrity limit for the AEAD algorithm used by
      the given connection.

   NO_VIABLE_PATH (0x10):  An endpoint has determined that the network
      path is incapable of supporting QUIC.  An endpoint is unlikely to
      receive a CONNECTION_CLOSE frame carrying this code except when
      the path does not support a large enough MTU.

   CRYPTO_ERROR (0x0100-0x01ff):  The cryptographic handshake failed.  A
      range of 256 values is reserved for carrying error codes specific
      to the cryptographic handshake that is used.  Codes for errors
      occurring when TLS is used for the cryptographic handshake are
      described in Section 4.8 of [QUIC-TLS].

   See Section 22.5 for details on registering new error codes.

   In defining these error codes, several principles are applied.  Error
   conditions that might require specific action on the part of a
   recipient are given unique codes.  Errors that represent common
   conditions are given specific codes.  Absent either of these
   conditions, error codes are used to identify a general function of
   the stack, like flow control or transport parameter handling.
   Finally, generic errors are provided for conditions where
   implementations are unable or unwilling to use more specific codes.

## 20.2.  Application Protocol Error Codes

   The management of application error codes is left to application
   protocols.  Application protocol error codes are used for the
   RESET_STREAM frame (Section 19.4), the STOP_SENDING frame
   (Section 19.5), and the CONNECTION_CLOSE frame with a type of 0x1d
   (Section 19.19).

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
