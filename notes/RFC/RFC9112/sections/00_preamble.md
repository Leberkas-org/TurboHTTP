---
title: "Preamble"
rfc_number: 9112
rfc_section: "preamble"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Preamble of RFC 9112 — HTTP/1.1 — Abstract, Status of This Memo, Copyright Notice"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, preamble]
---

## Preamble





Internet Engineering Task Force (IETF)                  R. Fielding, Ed.
Request for Comments: 9112                                         Adobe
STD: 99                                               M. Nottingham, Ed.
Obsoletes: 7230                                                   Fastly
Category: Standards Track                                J. Reschke, Ed.
ISSN: 2070-1721                                               greenbytes
                                                               June 2022


                                HTTP/1.1

Abstract

   The Hypertext Transfer Protocol (HTTP) is a stateless application-
   level protocol for distributed, collaborative, hypertext information
   systems.  This document specifies the HTTP/1.1 message syntax,
   message parsing, connection management, and related security
   concerns.

   This document obsoletes portions of RFC 7230.

Status of This Memo

   This is an Internet Standards Track document.

   This document is a product of the Internet Engineering Task Force
   (IETF).  It represents the consensus of the IETF community.  It has
   received public review and has been approved for publication by the
   Internet Engineering Steering Group (IESG).  Further information on
   Internet Standards is available in Section 2 of RFC 7841.

   Information about the current status of this document, any errata,
   and how to provide feedback on it may be obtained at
   https://www.rfc-editor.org/info/rfc9112.

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

   This document may contain material from IETF Documents or IETF
   Contributions published or made publicly available before November
   10, 2008.  The person(s) controlling the copyright in some of this
   material may not have granted the IETF Trust the right to allow
   modifications of such material outside the IETF Standards Process.
   Without obtaining an adequate license from the person(s) controlling
   the copyright in such materials, this document may not be modified
   outside the IETF Standards Process, and derivative works of it may
   not be created outside the IETF Standards Process, except to format
   it for publication as an RFC or to translate it into languages other
   than English.

Table of Contents

   1.  Introduction
     1.1.  Requirements Notation
     1.2.  Syntax Notation
   2.  Message
     2.1.  Message Format
     2.2.  Message Parsing
     2.3.  HTTP Version
   3.  Request Line
     3.1.  Method
     3.2.  Request Target
       3.2.1.  origin-form
       3.2.2.  absolute-form
       3.2.3.  authority-form
       3.2.4.  asterisk-form
     3.3.  Reconstructing the Target URI
   4.  Status Line
   5.  Field Syntax
     5.1.  Field Line Parsing
     5.2.  Obsolete Line Folding
   6.  Message Body
     6.1.  Transfer-Encoding
     6.2.  Content-Length
     6.3.  Message Body Length
   7.  Transfer Codings
     7.1.  Chunked Transfer Coding
       7.1.1.  Chunk Extensions
       7.1.2.  Chunked Trailer Section
       7.1.3.  Decoding Chunked
     7.2.  Transfer Codings for Compression
     7.3.  Transfer Coding Registry
     7.4.  Negotiating Transfer Codings
   8.  Handling Incomplete Messages
   9.  Connection Management
     9.1.  Establishment
     9.2.  Associating a Response to a Request
     9.3.  Persistence
       9.3.1.  Retrying Requests
       9.3.2.  Pipelining
     9.4.  Concurrency
     9.5.  Failures and Timeouts
     9.6.  Tear-down
     9.7.  TLS Connection Initiation
     9.8.  TLS Connection Closure
   10. Enclosing Messages as Data
     10.1.  Media Type message/http
     10.2.  Media Type application/http
   11. Security Considerations
     11.1.  Response Splitting
     11.2.  Request Smuggling
     11.3.  Message Integrity
     11.4.  Message Confidentiality
   12. IANA Considerations
     12.1.  Field Name Registration
     12.2.  Media Type Registration
     12.3.  Transfer Coding Registration
     12.4.  ALPN Protocol ID Registration
   13. References
     13.1.  Normative References
     13.2.  Informative References
   Appendix A.  Collected ABNF
   Appendix B.  Differences between HTTP and MIME
     B.1.  MIME-Version
     B.2.  Conversion to Canonical Form
     B.3.  Conversion of Date Formats
     B.4.  Conversion of Content-Encoding
     B.5.  Conversion of Content-Transfer-Encoding
     B.6.  MHTML and Line Length Limitations
   Appendix C.  Changes from Previous RFCs
     C.1.  Changes from HTTP/0.9
     C.2.  Changes from HTTP/1.0
       C.2.1.  Multihomed Web Servers
       C.2.2.  Keep-Alive Connections
       C.2.3.  Introduction of Transfer-Encoding
     C.3.  Changes from RFC 7230
   Acknowledgements
   Index
   Authors' Addresses

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
