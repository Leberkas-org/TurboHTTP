---
title: "2.  Compression Process Overview"
rfc_number: 9204
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 2: Compression Process Overview — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, compression_process_overview]
---

## 2.  Compression Process Overview

2.  Compression Process Overview

   Like HPACK, QPACK uses two tables for associating field lines
   ("headers") to indices.  The static table (Section 3.1) is predefined
   and contains common header field lines (some of them with an empty
   value).  The dynamic table (Section 3.2) is built up over the course
   of the connection and can be used by the encoder to index both header
   and trailer field lines in the encoded field sections.

   QPACK defines unidirectional streams for sending instructions from
   encoder to decoder and vice versa.

## 2.1  Encoder

   An encoder converts a header or trailer section into a series of
   representations by emitting either an indexed or a literal
   representation for each field line in the list; see Section 4.5.
   Indexed representations achieve high compression by replacing the
   literal name and possibly the value with an index to either the
   static or dynamic table.  References to the static table and literal
   representations do not require any dynamic state and never risk head-
   of-line blocking.  References to the dynamic table risk head-of-line
   blocking if the encoder has not received an acknowledgment indicating
   the entry is available at the decoder.

> **MAY**: An encoder MAY insert any entry in the dynamic table it chooses; it
   is not limited to field lines it is compressing.

   QPACK preserves the ordering of field lines within each field
> **MUST**: section.  An encoder MUST emit field representations in the order
   they appear in the input field section.

   QPACK is designed to place the burden of optional state tracking on
   the encoder, resulting in relatively simple decoders.

### 2.1.1  Limits on Dynamic Table Insertions

   Inserting entries into the dynamic table might not be possible if the
   table contains entries that cannot be evicted.

   A dynamic table entry cannot be evicted immediately after insertion,
   even if it has never been referenced.  Once the insertion of a
   dynamic table entry has been acknowledged and there are no
   outstanding references to the entry in unacknowledged
   representations, the entry becomes evictable.  Note that references
   on the encoder stream never preclude the eviction of an entry,
   because those references are guaranteed to be processed before the
   instruction evicting the entry.

   If the dynamic table does not contain enough room for a new entry
   without evicting other entries, and the entries that would be evicted
> **MUST NOT**: are not evictable, the encoder MUST NOT insert that entry into the
   dynamic table (including duplicates of existing entries).  In order
   to avoid this, an encoder that uses the dynamic table has to keep
   track of each dynamic table entry referenced by each field section
   until those representations are acknowledged by the decoder; see
   Section 4.4.1.

#### 2.1.1.1  Avoiding Prohibited Insertions

   To ensure that the encoder is not prevented from adding new entries,
   the encoder can avoid referencing entries that are close to eviction.
   Rather than reference such an entry, the encoder can emit a Duplicate
   instruction (Section 4.3.4) and reference the duplicate instead.

   Determining which entries are too close to eviction to reference is
   an encoder preference.  One heuristic is to target a fixed amount of
   available space in the dynamic table: either unused space or space
   that can be reclaimed by evicting non-blocking entries.  To achieve
   this, the encoder can maintain a draining index, which is the
   smallest absolute index (Section 3.2.4) in the dynamic table that it
   will emit a reference for.  As new entries are inserted, the encoder
   increases the draining index to maintain the section of the table
   that it will not reference.  If the encoder does not create new
   references to entries with an absolute index lower than the draining
   index, the number of unacknowledged references to those entries will
   eventually become zero, allowing them to be evicted.

                <-- Newer Entries          Older Entries -->
                  (Larger Indices)       (Smaller Indices)
      +--------+---------------------------------+----------+
      | Unused |          Referenceable          | Draining |
      | Space  |             Entries             | Entries  |
      +--------+---------------------------------+----------+
               ^                                 ^          ^
               |                                 |          |
         Insertion Point                 Draining Index  Dropping
                                                          Point

                  Figure 1: Draining Dynamic Table Entries

### 2.1.2  Blocked Streams

   Because QUIC does not guarantee order between data on different
   streams, a decoder might encounter a representation that references a
   dynamic table entry that it has not yet received.

   Each encoded field section contains a Required Insert Count
   (Section 4.5.1), the lowest possible value for the Insert Count with
   which the field section can be decoded.  For a field section encoded
   using references to the dynamic table, the Required Insert Count is
   one larger than the largest absolute index of all referenced dynamic
   table entries.  For a field section encoded with no references to the
   dynamic table, the Required Insert Count is zero.

   When the decoder receives an encoded field section with a Required
   Insert Count greater than its own Insert Count, the stream cannot be
   processed immediately and is considered "blocked"; see Section 2.2.1.

   The decoder specifies an upper bound on the number of streams that
   can be blocked using the SETTINGS_QPACK_BLOCKED_STREAMS setting; see
> **MUST**: Section 5.  An encoder MUST limit the number of streams that could
   become blocked to the value of SETTINGS_QPACK_BLOCKED_STREAMS at all
   times.  If a decoder encounters more blocked streams than it promised
> **MUST**: to support, it MUST treat this as a connection error of type
   QPACK_DECOMPRESSION_FAILED.

   Note that the decoder might not become blocked on every stream that
   risks becoming blocked.

   An encoder can decide whether to risk having a stream become blocked.
   If permitted by the value of SETTINGS_QPACK_BLOCKED_STREAMS,
   compression efficiency can often be improved by referencing dynamic
   table entries that are still in transit, but if there is loss or
   reordering, the stream can become blocked at the decoder.  An encoder
   can avoid the risk of blocking by only referencing dynamic table
   entries that have been acknowledged, but this could mean using
   literals.  Since literals make the encoded field section larger, this
   can result in the encoder becoming blocked on congestion or flow-
   control limits.

### 2.1.3  Avoiding Flow-Control Deadlocks

   Writing instructions on streams that are limited by flow control can
   produce deadlocks.

   A decoder might stop issuing flow-control credit on the stream that
   carries an encoded field section until the necessary updates are
   received on the encoder stream.  If the granting of flow-control
   credit on the encoder stream (or the connection as a whole) depends
   on the consumption and release of data on the stream carrying the
   encoded field section, a deadlock might result.

   More generally, a stream containing a large instruction can become
   deadlocked if the decoder withholds flow-control credit until the
   instruction is completely received.

> **SHOULD NOT**: To avoid these deadlocks, an encoder SHOULD NOT write an instruction
   unless sufficient stream and connection flow-control credit is
   available for the entire instruction.

### 2.1.4  Known Received Count

   The Known Received Count is the total number of dynamic table
   insertions and duplications acknowledged by the decoder.  The encoder
   tracks the Known Received Count in order to identify which dynamic
   table entries can be referenced without potentially blocking a
   stream.  The decoder tracks the Known Received Count in order to be
   able to send Insert Count Increment instructions.

   A Section Acknowledgment instruction (Section 4.4.1) implies that the
   decoder has received all dynamic table state necessary to decode the
   field section.  If the Required Insert Count of the acknowledged
   field section is greater than the current Known Received Count, the
   Known Received Count is updated to that Required Insert Count value.

   An Insert Count Increment instruction (Section 4.4.3) increases the
   Known Received Count by its Increment parameter.  See Section 2.2.2.3
   for guidance.

## 2.2  Decoder

   As in HPACK, the decoder processes a series of representations and
   emits the corresponding field sections.  It also processes
   instructions received on the encoder stream that modify the dynamic
   table.  Note that encoded field sections and encoder stream
   instructions arrive on separate streams.  This is unlike HPACK, where
   encoded field sections (header blocks) can contain instructions that
   modify the dynamic table, and there is no dedicated stream of HPACK
   instructions.

> **MUST**: The decoder MUST emit field lines in the order their representations
   appear in the encoded field section.

### 2.2.1  Blocked Decoding

   Upon receipt of an encoded field section, the decoder examines the
   Required Insert Count.  When the Required Insert Count is less than
   or equal to the decoder's Insert Count, the field section can be
   processed immediately.  Otherwise, the stream on which the field
   section was received becomes blocked.

> **SHOULD**: While blocked, encoded field section data SHOULD remain in the
   blocked stream's flow-control window.  This data is unusable until
   the stream becomes unblocked, and releasing the flow control
   prematurely makes the decoder vulnerable to memory exhaustion
   attacks.  A stream becomes unblocked when the Insert Count becomes
   greater than or equal to the Required Insert Count for all encoded
   field sections the decoder has started reading from the stream.

   When processing encoded field sections, the decoder expects the
   Required Insert Count to equal the lowest possible value for the
   Insert Count with which the field section can be decoded, as
   prescribed in Section 2.1.2.  If it encounters a Required Insert
> **MUST**: Count smaller than expected, it MUST treat this as a connection error
   of type QPACK_DECOMPRESSION_FAILED; see Section 2.2.3.  If it
> **MAY**: encounters a Required Insert Count larger than expected, it MAY treat
   this as a connection error of type QPACK_DECOMPRESSION_FAILED.

### 2.2.2  State Synchronization

   The decoder signals the following events by emitting decoder
   instructions (Section 4.4) on the decoder stream.

#### 2.2.2.1  Completed Processing of a Field Section

   After the decoder finishes decoding a field section encoded using
> **MUST**: representations containing dynamic table references, it MUST emit a
   Section Acknowledgment instruction (Section 4.4.1).  A stream may
   carry multiple field sections in the case of intermediate responses,
   trailers, and pushed requests.  The encoder interprets each
   Section Acknowledgment instruction as acknowledging the earliest
   unacknowledged field section containing dynamic table references sent
   on the given stream.

#### 2.2.2.2  Abandonment of a Stream

   When an endpoint receives a stream reset before the end of a stream
   or before all encoded field sections are processed on that stream, or
   when it abandons reading of a stream, it generates a Stream
   Cancellation instruction; see Section 4.4.2.  This signals to the
   encoder that all references to the dynamic table on that stream are
   no longer outstanding.  A decoder with a maximum dynamic table
> **MAY**: capacity (Section 3.2.3) equal to zero MAY omit sending Stream
   Cancellations, because the encoder cannot have any dynamic table
   references.  An encoder cannot infer from this instruction that any
   updates to the dynamic table have been received.

   The Section Acknowledgment and Stream Cancellation instructions
   permit the encoder to remove references to entries in the dynamic
   table.  When an entry with an absolute index lower than the Known
   Received Count has zero references, then it is considered evictable;
   see Section 2.1.1.

#### 2.2.2.3  New Table Entries

   After receiving new table entries on the encoder stream, the decoder
   chooses when to emit Insert Count Increment instructions; see
   Section 4.4.3.  Emitting this instruction after adding each new
   dynamic table entry will provide the timeliest feedback to the
   encoder, but could be redundant with other decoder feedback.  By
   delaying an Insert Count Increment instruction, the decoder might be
   able to coalesce multiple Insert Count Increment instructions or
   replace them entirely with Section Acknowledgments; see
   Section 4.4.1.  However, delaying too long may lead to compression
   inefficiencies if the encoder waits for an entry to be acknowledged
   before using it.

### 2.2.3  Invalid References

   If the decoder encounters a reference in a field line representation
   to a dynamic table entry that has already been evicted or that has an
   absolute index greater than or equal to the declared Required Insert
> **MUST**: Count (Section 4.5.1), it MUST treat this as a connection error of
   type QPACK_DECOMPRESSION_FAILED.

   If the decoder encounters a reference in an encoder instruction to a
> **MUST**: dynamic table entry that has already been evicted, it MUST treat this
   as a connection error of type QPACK_ENCODER_STREAM_ERROR.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
