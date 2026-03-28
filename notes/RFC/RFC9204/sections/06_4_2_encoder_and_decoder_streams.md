---
title: "4.2.  Encoder and Decoder Streams"
rfc_number: 9204
rfc_section: "4.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 4.2: Encoder and Decoder Streams — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, encoder_and_decoder_streams]
---

## 4.2.  Encoder and Decoder Streams

## 4.2  Encoder and Decoder Streams

   QPACK defines two unidirectional stream types:

   *  An encoder stream is a unidirectional stream of type 0x02.  It
      carries an unframed sequence of encoder instructions from encoder
      to decoder.

   *  A decoder stream is a unidirectional stream of type 0x03.  It
      carries an unframed sequence of decoder instructions from decoder
      to encoder.

   HTTP/3 endpoints contain a QPACK encoder and decoder.  Each endpoint
> **MUST**: MUST initiate, at most, one encoder stream and, at most, one decoder
   stream.  Receipt of a second instance of either stream type MUST be
   treated as a connection error of type H3_STREAM_CREATION_ERROR.

> **MUST NOT**: The sender MUST NOT close either of these streams, and the receiver
   MUST NOT request that the sender close either of these streams.
> **MUST**: Closure of either unidirectional stream type MUST be treated as a
   connection error of type H3_CLOSED_CRITICAL_STREAM.

> **MAY**: An endpoint MAY avoid creating an encoder stream if it will not be
   used (for example, if its encoder does not wish to use the dynamic
   table or if the maximum size of the dynamic table permitted by the
   peer is zero).

> **MAY**: An endpoint MAY avoid creating a decoder stream if its decoder sets
   the maximum capacity of the dynamic table to zero.

> **MUST**: An endpoint MUST allow its peer to create an encoder stream and a
   decoder stream even if the connection's settings prevent their use.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
