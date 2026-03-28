---
title: "Preamble"
rfc_number: 7541
rfc_section: "preamble"
source_url: "https://www.rfc-editor.org/rfc/rfc7541"
description: "Preamble of RFC 7541 — HPACK: Header Compression for HTTP/2 — Abstract, Status of This Memo, Copyright Notice"
tags: [RFC7541, HPACK, header-compression, HTTP/2, dynamic-table, static-table, Huffman-coding, indexed-representation, preamble]
---

# Preamble







Internet Engineering Task Force (IETF)                           R. Peon
Request for Comments: 7541                                   Google, Inc
Category: Standards Track                                     H. Ruellan
ISSN: 2070-1721                                                Canon CRF
                                                                May 2015


                  HPACK: Header Compression for HTTP/2

Abstract

   This specification defines HPACK, a compression format for
   efficiently representing HTTP header fields, to be used in HTTP/2.

Status of This Memo

   This is an Internet Standards Track document.

   This document is a product of the Internet Engineering Task Force
   (IETF).  It represents the consensus of the IETF community.  It has
   received public review and has been approved for publication by the
   Internet Engineering Steering Group (IESG).  Further information on
   Internet Standards is available in Section 2 of RFC 5741.

   Information about the current status of this document, any errata,
   and how to provide feedback on it may be obtained at
   http://www.rfc-editor.org/info/rfc7541.

Copyright Notice

   Copyright (c) 2015 IETF Trust and the persons identified as the
   document authors.  All rights reserved.

   This document is subject to BCP 78 and the IETF Trust's Legal
   Provisions Relating to IETF Documents
   (http://trustee.ietf.org/license-info) in effect on the date of
   publication of this document.  Please review these documents
   carefully, as they describe your rights and restrictions with respect
   to this document.  Code Components extracted from this document must
   include Simplified BSD License text as described in Section 4.e of
   the Trust Legal Provisions and are provided without warranty as
   described in the Simplified BSD License.









Table of Contents

   1. Introduction ....................................................4
      1.1. Overview ...................................................4
      1.2. Conventions ................................................5
      1.3. Terminology ................................................5
   2. Compression Process Overview ....................................6
      2.1. Header List Ordering .......................................6
      2.2. Encoding and Decoding Contexts .............................6
      2.3. Indexing Tables ............................................6
           2.3.1. Static Table ........................................6
           2.3.2. Dynamic Table .......................................6
           2.3.3. Index Address Space .................................7
      2.4. Header Field Representation ................................8
   3. Header Block Decoding ...........................................8
      3.1. Header Block Processing ....................................8
      3.2. Header Field Representation Processing .....................9
   4. Dynamic Table Management ........................................9
      4.1. Calculating Table Size ....................................10
      4.2. Maximum Table Size ........................................10
      4.3. Entry Eviction When Dynamic Table Size Changes ............11
      4.4. Entry Eviction When Adding New Entries ....................11
   5. Primitive Type Representations .................................11
      5.1. Integer Representation ....................................11
      5.2. String Literal Representation .............................13
   6. Binary Format ..................................................14
      6.1. Indexed Header Field Representation .......................14
      6.2. Literal Header Field Representation .......................15
           6.2.1. Literal Header Field with Incremental Indexing .....15
           6.2.2. Literal Header Field without Indexing ..............16
           6.2.3. Literal Header Field Never Indexed .................17
      6.3. Dynamic Table Size Update .................................18
   7. Security Considerations ........................................19
      7.1. Probing Dynamic Table State ...............................19
           7.1.1. Applicability to HPACK and HTTP ....................20
           7.1.2. Mitigation .........................................20
           7.1.3. Never-Indexed Literals .............................21
      7.2. Static Huffman Encoding ...................................22
      7.3. Memory Consumption ........................................22
      7.4. Implementation Limits .....................................23
   8. References .....................................................23
      8.1. Normative References ......................................23
      8.2. Informative References ....................................24
   Appendix A. Static Table Definition ...............................25
   Appendix B. Huffman Code ..........................................27






   Appendix C. Examples ..............................................33
     C.1. Integer Representation Examples ............................33
       C.1.1. Example 1: Encoding 10 Using a 5-Bit Prefix ............33
       C.1.2. Example 2: Encoding 1337 Using a 5-Bit Prefix ..........33
       C.1.3. Example 3: Encoding 42 Starting at an Octet Boundary ...34
     C.2. Header Field Representation Examples .......................34
       C.2.1. Literal Header Field with Indexing .....................34
       C.2.2. Literal Header Field without Indexing ..................35
       C.2.3. Literal Header Field Never Indexed .....................36
       C.2.4. Indexed Header Field ...................................37
     C.3. Request Examples without Huffman Coding ....................37
       C.3.1. First Request ..........................................37
       C.3.2. Second Request .........................................38
       C.3.3. Third Request ..........................................39
     C.4. Request Examples with Huffman Coding .......................41
       C.4.1. First Request ..........................................41
       C.4.2. Second Request .........................................42
       C.4.3. Third Request ..........................................43
     C.5. Response Examples without Huffman Coding ...................45
       C.5.1. First Response .........................................45
       C.5.2. Second Response ........................................46
       C.5.3. Third Response .........................................47
     C.6. Response Examples with Huffman Coding ......................49
       C.6.1. First Response .........................................49
       C.6.2. Second Response ........................................51
       C.6.3. Third Response .........................................52
   Acknowledgments ...................................................55
   Authors' Addresses ................................................55

---

**Navigation:** [[../RFC7541|RFC7541 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
