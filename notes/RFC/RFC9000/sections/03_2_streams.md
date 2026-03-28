---
title: "2.  Streams"
rfc_number: 9000
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 2: Streams — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, streams]
---

# 2.  Streams


   Streams in QUIC provide a lightweight, ordered byte-stream
   abstraction to an application.  Streams can be unidirectional or
   bidirectional.

   Streams can be created by sending data.  Other processes associated
   with stream management -- ending, canceling, and managing flow
   control -- are all designed to impose minimal overheads.  For
   instance, a single STREAM frame (Section 19.8) can open, carry data
   for, and close a stream.  Streams can also be long-lived and can last
   the entire duration of a connection.

   Streams can be created by either endpoint, can concurrently send data
   interleaved with other streams, and can be canceled.  QUIC does not
   provide any means of ensuring ordering between bytes on different
   streams.

   QUIC allows for an arbitrary number of streams to operate
   concurrently and for an arbitrary amount of data to be sent on any
   stream, subject to flow control constraints and stream limits; see
   Section 4.

## 2.1.  Stream Types and Identifiers

   Streams can be unidirectional or bidirectional.  Unidirectional
   streams carry data in one direction: from the initiator of the stream
   to its peer.  Bidirectional streams allow for data to be sent in both
   directions.

   Streams are identified within a connection by a numeric value,
   referred to as the stream ID.  A stream ID is a 62-bit integer (0 to
   2^62-1) that is unique for all streams on a connection.  Stream IDs
   are encoded as variable-length integers; see Section 16.  A QUIC
> **MUST NOT**: endpoint MUST NOT reuse a stream ID within a connection.

   The least significant bit (0x01) of the stream ID identifies the
   initiator of the stream.  Client-initiated streams have even-numbered
   stream IDs (with the bit set to 0), and server-initiated streams have
   odd-numbered stream IDs (with the bit set to 1).

   The second least significant bit (0x02) of the stream ID
   distinguishes between bidirectional streams (with the bit set to 0)
   and unidirectional streams (with the bit set to 1).

   The two least significant bits from a stream ID therefore identify a
   stream as one of four types, as summarized in Table 1.

                +======+==================================+
                | Bits | Stream Type                      |
                +======+==================================+
                | 0x00 | Client-Initiated, Bidirectional  |
                +------+----------------------------------+
                | 0x01 | Server-Initiated, Bidirectional  |
                +------+----------------------------------+
                | 0x02 | Client-Initiated, Unidirectional |
                +------+----------------------------------+
                | 0x03 | Server-Initiated, Unidirectional |
                +------+----------------------------------+

                          Table 1: Stream ID Types

   The stream space for each type begins at the minimum value (0x00
   through 0x03, respectively); successive streams of each type are
   created with numerically increasing stream IDs.  A stream ID that is
   used out of order results in all streams of that type with lower-
   numbered stream IDs also being opened.

## 2.2.  Sending and Receiving Data

   STREAM frames (Section 19.8) encapsulate data sent by an application.
   An endpoint uses the Stream ID and Offset fields in STREAM frames to
   place data in order.

> **MUST**: Endpoints MUST be able to deliver stream data to an application as an
   ordered byte stream.  Delivering an ordered byte stream requires that
   an endpoint buffer any data that is received out of order, up to the
   advertised flow control limit.

   QUIC makes no specific allowances for delivery of stream data out of
> **MAY**: order.  However, implementations MAY choose to offer the ability to
   deliver data out of order to a receiving application.

   An endpoint could receive data for a stream at the same stream offset
   multiple times.  Data that has already been received can be
> **MUST NOT**: discarded.  The data at a given offset MUST NOT change if it is sent
   multiple times; an endpoint MAY treat receipt of different data at
   the same offset within a stream as a connection error of type
   PROTOCOL_VIOLATION.

   Streams are an ordered byte-stream abstraction with no other
   structure visible to QUIC.  STREAM frame boundaries are not expected
   to be preserved when data is transmitted, retransmitted after packet
   loss, or delivered to the application at a receiver.

> **MUST NOT**: An endpoint MUST NOT send data on any stream without ensuring that it
   is within the flow control limits set by its peer.  Flow control is
   described in detail in Section 4.

## 2.3.  Stream Prioritization

   Stream multiplexing can have a significant effect on application
   performance if resources allocated to streams are correctly
   prioritized.

   QUIC does not provide a mechanism for exchanging prioritization
   information.  Instead, it relies on receiving priority information
   from the application.

> **SHOULD**: A QUIC implementation SHOULD provide ways in which an application can
   indicate the relative priority of streams.  An implementation uses
   information provided by the application to determine how to allocate
   resources to active streams.

## 2.4.  Operations on Streams

   This document does not define an API for QUIC; it instead defines a
   set of functions on streams that application protocols can rely upon.
   An application protocol can assume that a QUIC implementation
   provides an interface that includes the operations described in this
   section.  An implementation designed for use with a specific
   application protocol might provide only those operations that are
   used by that protocol.

   On the sending part of a stream, an application protocol can:

   *  write data, understanding when stream flow control credit
      (Section 4.1) has successfully been reserved to send the written
      data;

   *  end the stream (clean termination), resulting in a STREAM frame
      (Section 19.8) with the FIN bit set; and

   *  reset the stream (abrupt termination), resulting in a RESET_STREAM
      frame (Section 19.4) if the stream was not already in a terminal
      state.

   On the receiving part of a stream, an application protocol can:

   *  read data; and

   *  abort reading of the stream and request closure, possibly
      resulting in a STOP_SENDING frame (Section 19.5).

   An application protocol can also request to be informed of state
   changes on streams, including when the peer has opened or reset a
   stream, when a peer aborts reading on a stream, when new data is
   available, and when data can or cannot be written to the stream due
   to flow control.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
