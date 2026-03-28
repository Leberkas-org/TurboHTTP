---
title: "7.5.  Cryptographic Message Buffering"
rfc_number: 9000
rfc_section: "7.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 7.5: Cryptographic Message Buffering — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, cryptographic_message_buffering]
---

# 7.5.  Cryptographic Message Buffering


   Implementations need to maintain a buffer of CRYPTO data received out
   of order.  Because there is no flow control of CRYPTO frames, an
   endpoint could potentially force its peer to buffer an unbounded
   amount of data.

> **MUST**: Implementations MUST support buffering at least 4096 bytes of data
   received in out-of-order CRYPTO frames.  Endpoints MAY choose to
   allow more data to be buffered during the handshake.  A larger limit
   during the handshake could allow for larger keys or credentials to be
   exchanged.  An endpoint's buffer size does not need to remain
   constant during the life of the connection.

   Being unable to buffer CRYPTO frames during the handshake can lead to
   a connection failure.  If an endpoint's buffer is exceeded during the
   handshake, it can expand its buffer temporarily to complete the
> **MUST**: handshake.  If an endpoint does not expand its buffer, it MUST close
   the connection with a CRYPTO_BUFFER_EXCEEDED error code.

   Once the handshake completes, if an endpoint is unable to buffer all
> **MAY**: data in a CRYPTO frame, it MAY discard that CRYPTO frame and all
   CRYPTO frames received in the future, or it MAY close the connection
   with a CRYPTO_BUFFER_EXCEEDED error code.  Packets containing
> **MUST**: discarded CRYPTO frames MUST be acknowledged because the packet has
   been received and processed by the transport even though the CRYPTO
   frame was discarded.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
