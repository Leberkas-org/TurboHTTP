---
title: "Preamble"
rfc_number: 9204
rfc_section: "preamble"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Preamble of RFC 9204 — QPACK: Field Compression for HTTP/3 — Abstract, Status of This Memo, Copyright Notice"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, preamble]
---

## Preamble





Internet Engineering Task Force (IETF)                         C. Krasic
Request for Comments: 9204                                              
Category: Standards Track                                      M. Bishop
ISSN: 2070-1721                                      Akamai Technologies
                                                        A. Frindell, Ed.
                                                                Facebook
                                                               June 2022


                  QPACK: Field Compression for HTTP/3

Abstract

   This specification defines QPACK: a compression format for
   efficiently representing HTTP fields that is to be used in HTTP/3.
   This is a variation of HPACK compression that seeks to reduce head-
   of-line blocking.

Status of This Memo

   This is an Internet Standards Track document.

   This document is a product of the Internet Engineering Task Force
   (IETF).  It represents the consensus of the IETF community.  It has
   received public review and has been approved for publication by the
   Internet Engineering Steering Group (IESG).  Further information on
   Internet Standards is available in Section 2 of RFC 7841.

   Information about the current status of this document, any errata,
   and how to provide feedback on it may be obtained at
   https://www.rfc-editor.org/info/rfc9204.

Copyright Notice

   Copyright (c) 2022 IETF Trust and the persons identified as the
   document authors.  All rights reserved.

   This document is subject to BCP 78 and the IETF Trust's Legal
   Provisions Relating to IETF Documents
   (https://trustee.ietf.org/license-info) in effect on the date of
   publication of this document.  Please review these documents
   carefully, as they describe your rights and restrictions with respect
   to this document.  Code Components extracted from this document must
   include Revised BSD License text as described in Section 4.e of the
   Trust Legal Provisions and are provided without warranty as described
   in the Revised BSD License.

Table of Contents

   1.  Introduction
     1.1.  Conventions and Definitions
     1.2.  Notational Conventions
   2.  Compression Process Overview
     2.1.  Encoder
       2.1.1.  Limits on Dynamic Table Insertions
       2.1.2.  Blocked Streams
       2.1.3.  Avoiding Flow-Control Deadlocks
       2.1.4.  Known Received Count
     2.2.  Decoder
       2.2.1.  Blocked Decoding
       2.2.2.  State Synchronization
       2.2.3.  Invalid References
   3.  Reference Tables
     3.1.  Static Table
     3.2.  Dynamic Table
       3.2.1.  Dynamic Table Size
       3.2.2.  Dynamic Table Capacity and Eviction
       3.2.3.  Maximum Dynamic Table Capacity
       3.2.4.  Absolute Indexing
       3.2.5.  Relative Indexing
       3.2.6.  Post-Base Indexing
   4.  Wire Format
     4.1.  Primitives
       4.1.1.  Prefixed Integers
       4.1.2.  String Literals
     4.2.  Encoder and Decoder Streams
     4.3.  Encoder Instructions
       4.3.1.  Set Dynamic Table Capacity
       4.3.2.  Insert with Name Reference
       4.3.3.  Insert with Literal Name
       4.3.4.  Duplicate
     4.4.  Decoder Instructions
       4.4.1.  Section Acknowledgment
       4.4.2.  Stream Cancellation
       4.4.3.  Insert Count Increment
     4.5.  Field Line Representations
       4.5.1.  Encoded Field Section Prefix
       4.5.2.  Indexed Field Line
       4.5.3.  Indexed Field Line with Post-Base Index
       4.5.4.  Literal Field Line with Name Reference
       4.5.5.  Literal Field Line with Post-Base Name Reference
       4.5.6.  Literal Field Line with Literal Name
   5.  Configuration
   6.  Error Handling
   7.  Security Considerations
     7.1.  Probing Dynamic Table State
       7.1.1.  Applicability to QPACK and HTTP
       7.1.2.  Mitigation
       7.1.3.  Never-Indexed Literals
     7.2.  Static Huffman Encoding
     7.3.  Memory Consumption
     7.4.  Implementation Limits
   8.  IANA Considerations
     8.1.  Settings Registration
     8.2.  Stream Type Registration
     8.3.  Error Code Registration
   9.  References
     9.1.  Normative References
     9.2.  Informative References
   Appendix A.  Static Table
   Appendix B.  Encoding and Decoding Examples
     B.1.  Literal Field Line with Name Reference
     B.2.  Dynamic Table
     B.3.  Speculative Insert
     B.4.  Duplicate Instruction, Stream Cancellation
     B.5.  Dynamic Table Insert, Eviction
   Appendix C.  Sample Single-Pass Encoding Algorithm
   Acknowledgments
   Authors' Addresses

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
