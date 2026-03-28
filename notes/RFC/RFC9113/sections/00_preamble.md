---
title: "Preamble"
rfc_number: 9113
rfc_section: "preamble"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Preamble of RFC 9113 — HTTP/2 — Abstract, Status of This Memo, Copyright Notice"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, preamble]
---

## Preamble





Internet Engineering Task Force (IETF)                   M. Thomson, Ed.
Request for Comments: 9113                                       Mozilla
Obsoletes: 7540, 8740                                   C. Benfield, Ed.
Category: Standards Track                                     Apple Inc.
ISSN: 2070-1721                                                June 2022


                                 HTTP/2

Abstract

   This specification describes an optimized expression of the semantics
   of the Hypertext Transfer Protocol (HTTP), referred to as HTTP
   version 2 (HTTP/2).  HTTP/2 enables a more efficient use of network
   resources and a reduced latency by introducing field compression and
   allowing multiple concurrent exchanges on the same connection.

   This document obsoletes RFCs 7540 and 8740.

Status of This Memo

   This is an Internet Standards Track document.

   This document is a product of the Internet Engineering Task Force
   (IETF).  It represents the consensus of the IETF community.  It has
   received public review and has been approved for publication by the
   Internet Engineering Steering Group (IESG).  Further information on
   Internet Standards is available in Section 2 of RFC 7841.

   Information about the current status of this document, any errata,
   and how to provide feedback on it may be obtained at
   https://www.rfc-editor.org/info/rfc9113.

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
   2.  HTTP/2 Protocol Overview
     2.1.  Document Organization
     2.2.  Conventions and Terminology
   3.  Starting HTTP/2
     3.1.  HTTP/2 Version Identification
     3.2.  Starting HTTP/2 for "https" URIs
     3.3.  Starting HTTP/2 with Prior Knowledge
     3.4.  HTTP/2 Connection Preface
   4.  HTTP Frames
     4.1.  Frame Format
     4.2.  Frame Size
     4.3.  Field Section Compression and Decompression
       4.3.1.  Compression State
   5.  Streams and Multiplexing
     5.1.  Stream States
       5.1.1.  Stream Identifiers
       5.1.2.  Stream Concurrency
     5.2.  Flow Control
       5.2.1.  Flow-Control Principles
       5.2.2.  Appropriate Use of Flow Control
       5.2.3.  Flow-Control Performance
     5.3.  Prioritization
       5.3.1.  Background on Priority in RFC 7540
       5.3.2.  Priority Signaling in This Document
     5.4.  Error Handling
       5.4.1.  Connection Error Handling
       5.4.2.  Stream Error Handling
       5.4.3.  Connection Termination
     5.5.  Extending HTTP/2
   6.  Frame Definitions
     6.1.  DATA
     6.2.  HEADERS
     6.3.  PRIORITY
     6.4.  RST_STREAM
     6.5.  SETTINGS
       6.5.1.  SETTINGS Format
       6.5.2.  Defined Settings
       6.5.3.  Settings Synchronization
     6.6.  PUSH_PROMISE
     6.7.  PING
     6.8.  GOAWAY
     6.9.  WINDOW_UPDATE
       6.9.1.  The Flow-Control Window
       6.9.2.  Initial Flow-Control Window Size
       6.9.3.  Reducing the Stream Window Size
     6.10. CONTINUATION
   7.  Error Codes
   8.  Expressing HTTP Semantics in HTTP/2
     8.1.  HTTP Message Framing
       8.1.1.  Malformed Messages
     8.2.  HTTP Fields
       8.2.1.  Field Validity
       8.2.2.  Connection-Specific Header Fields
       8.2.3.  Compressing the Cookie Header Field
     8.3.  HTTP Control Data
       8.3.1.  Request Pseudo-Header Fields
       8.3.2.  Response Pseudo-Header Fields
     8.4.  Server Push
       8.4.1.  Push Requests
       8.4.2.  Push Responses
     8.5.  The CONNECT Method
     8.6.  The Upgrade Header Field
     8.7.  Request Reliability
     8.8.  Examples
       8.8.1.  Simple Request
       8.8.2.  Simple Response
       8.8.3.  Complex Request
       8.8.4.  Response with Body
       8.8.5.  Informational Responses
   9.  HTTP/2 Connections
     9.1.  Connection Management
       9.1.1.  Connection Reuse
     9.2.  Use of TLS Features
       9.2.1.  TLS 1.2 Features
       9.2.2.  TLS 1.2 Cipher Suites
       9.2.3.  TLS 1.3 Features
   10. Security Considerations
     10.1.  Server Authority
     10.2.  Cross-Protocol Attacks
     10.3.  Intermediary Encapsulation Attacks
     10.4.  Cacheability of Pushed Responses
     10.5.  Denial-of-Service Considerations
       10.5.1.  Limits on Field Block Size
       10.5.2.  CONNECT Issues
     10.6.  Use of Compression
     10.7.  Use of Padding
     10.8.  Privacy Considerations
     10.9.  Remote Timing Attacks
   11. IANA Considerations
     11.1.  HTTP2-Settings Header Field Registration
     11.2.  The h2c Upgrade Token
   12. References
     12.1.  Normative References
     12.2.  Informative References
   Appendix A.  Prohibited TLS 1.2 Cipher Suites
   Appendix B.  Changes from RFC 7540
   Acknowledgments
   Contributors
   Authors' Addresses

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
