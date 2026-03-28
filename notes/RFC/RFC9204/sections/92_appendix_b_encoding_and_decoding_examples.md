---
title: "Appendix B.  Encoding and Decoding Examples"
rfc_number: 9204
rfc_section: "Appendix B"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Appendix B: Encoding and Decoding Examples — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, encoding_and_decoding_examples]
---

## Appendix B.  Encoding and Decoding Examples

Appendix B.  Encoding and Decoding Examples

   The following examples represent a series of exchanges between an
   encoder and a decoder.  The exchanges are designed to exercise most
   QPACK instructions and highlight potentially common patterns and
   their impact on dynamic table state.  The encoder sends three encoded
   field sections containing one field line each, as well as two
   speculative inserts that are not referenced.

   The state of the encoder's dynamic table is shown, along with its
   current size.  Each entry is shown with the Absolute Index of the
   entry (Abs), the current number of outstanding encoded field sections
   with references to that entry (Ref), along with the name and value.
   Entries above the 'acknowledged' line have been acknowledged by the
   decoder.

B.1.  Literal Field Line with Name Reference

   The encoder sends an encoded field section containing a literal
   representation of a field with a static name reference.

   Data                | Interpretation
                                | Encoder's Dynamic Table

   Stream: 0
   0000                | Required Insert Count = 0, Base = 0
   510b 2f69 6e64 6578 | Literal Field Line with Name Reference
   2e68 746d 6c        |  Static Table, Index=1
                       |  (:path=/index.html)

                                 Abs Ref Name        Value
                                 ^-- acknowledged --^
                                 Size=0

B.2.  Dynamic Table

   The encoder sets the dynamic table capacity, inserts a header with a
   dynamic name reference, then sends a potentially blocking, encoded
   field section referencing this new entry.  The decoder acknowledges
   processing the encoded field section, which implicitly acknowledges
   all dynamic table insertions up to the Required Insert Count.

   Stream: Encoder
   3fbd01              | Set Dynamic Table Capacity=220
   c00f 7777 772e 6578 | Insert With Name Reference
   616d 706c 652e 636f | Static Table, Index=0
   6d                  |  (:authority=www.example.com)
   c10c 2f73 616d 706c | Insert With Name Reference
   652f 7061 7468      |  Static Table, Index=1
                       |  (:path=/sample/path)

                                 Abs Ref Name        Value
                                 ^-- acknowledged --^
                                  0   0  :authority  www.example.com
                                  1   0  :path       /sample/path
                                 Size=106

   Stream: 4
   0381                | Required Insert Count = 2, Base = 0
   10                  | Indexed Field Line With Post-Base Index
                       |  Absolute Index = Base(0) + Index(0) = 0
                       |  (:authority=www.example.com)
   11                  | Indexed Field Line With Post-Base Index
                       |  Absolute Index = Base(0) + Index(1) = 1
                       |  (:path=/sample/path)

                                 Abs Ref Name        Value
                                 ^-- acknowledged --^
                                  0   1  :authority  www.example.com
                                  1   1  :path       /sample/path
                                 Size=106

   Stream: Decoder
   84                  | Section Acknowledgment (stream=4)

                                 Abs Ref Name        Value
                                  0   0  :authority  www.example.com
                                  1   0  :path       /sample/path
                                 ^-- acknowledged --^
                                 Size=106

B.3.  Speculative Insert

   The encoder inserts a header into the dynamic table with a literal
   name.  The decoder acknowledges receipt of the entry.  The encoder
   does not send any encoded field sections.

   Stream: Encoder
   4a63 7573 746f 6d2d | Insert With Literal Name
   6b65 790c 6375 7374 |  (custom-key=custom-value)
   6f6d 2d76 616c 7565 |

                                 Abs Ref Name        Value
                                  0   0  :authority  www.example.com
                                  1   0  :path       /sample/path
                                 ^-- acknowledged --^
                                  2   0  custom-key  custom-value
                                 Size=160

   Stream: Decoder
   01                  | Insert Count Increment (1)

                                 Abs Ref Name        Value
                                  0   0  :authority  www.example.com
                                  1   0  :path       /sample/path
                                  2   0  custom-key  custom-value
                                 ^-- acknowledged --^
                                 Size=160

B.4.  Duplicate Instruction, Stream Cancellation

   The encoder duplicates an existing entry in the dynamic table, then
   sends an encoded field section referencing the dynamic table entries
   including the duplicated entry.  The packet containing the encoder
   stream data is delayed.  Before the packet arrives, the decoder
   cancels the stream and notifies the encoder that the encoded field
   section was not processed.

   Stream: Encoder
   02                  | Duplicate (Relative Index = 2)
                       |  Absolute Index =
                       |   Insert Count(3) - Index(2) - 1 = 0

                                 Abs Ref Name        Value
                                  0   0  :authority  www.example.com
                                  1   0  :path       /sample/path
                                  2   0  custom-key  custom-value
                                 ^-- acknowledged --^
                                  3   0  :authority  www.example.com
                                 Size=217

   Stream: 8
   0500                | Required Insert Count = 4, Base = 4
   80                  | Indexed Field Line, Dynamic Table
                       |  Absolute Index = Base(4) - Index(0) - 1 = 3
                       |  (:authority=www.example.com)
   c1                  | Indexed Field Line, Static Table Index = 1
                       |  (:path=/)
   81                  | Indexed Field Line, Dynamic Table
                       |  Absolute Index = Base(4) - Index(1) - 1 = 2
                       |  (custom-key=custom-value)

                                 Abs Ref Name        Value
                                  0   0  :authority  www.example.com
                                  1   0  :path       /sample/path
                                  2   1  custom-key  custom-value
                                 ^-- acknowledged --^
                                  3   1  :authority  www.example.com
                                 Size=217

   Stream: Decoder
   48                  | Stream Cancellation (Stream=8)

                                 Abs Ref Name        Value
                                  0   0  :authority  www.example.com
                                  1   0  :path       /sample/path
                                  2   0  custom-key  custom-value
                                 ^-- acknowledged --^
                                  3   0  :authority  www.example.com
                                 Size=217

B.5.  Dynamic Table Insert, Eviction

   The encoder inserts another header into the dynamic table, which
   evicts the oldest entry.  The encoder does not send any encoded field
   sections.

   Stream: Encoder
   810d 6375 7374 6f6d | Insert With Name Reference
   2d76 616c 7565 32   |  Dynamic Table, Relative Index = 1
                       |  Absolute Index =
                       |   Insert Count(4) - Index(1) - 1 = 2
                       |  (custom-key=custom-value2)

                                 Abs Ref Name        Value
                                  1   0  :path       /sample/path
                                  2   0  custom-key  custom-value
                                 ^-- acknowledged --^
                                  3   0  :authority  www.example.com
                                  4   0  custom-key  custom-value2
                                 Size=215

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
