---
title: "Preamble"
rfc_number: 6265
rfc_section: "preamble"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Preamble of RFC 6265 — HTTP State Management (Cookies) — Abstract, Status of This Memo, Copyright Notice"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, preamble]
---

# Preamble







Internet Engineering Task Force (IETF)                          A. Barth
Request for Comments: 6265                                 U.C. Berkeley
Obsoletes: 2965                                               April 2011
Category: Standards Track
ISSN: 2070-1721


                    HTTP State Management Mechanism

Abstract

   This document defines the HTTP Cookie and Set-Cookie header fields.
   These header fields can be used by HTTP servers to store state
   (called cookies) at HTTP user agents, letting the servers maintain a
   stateful session over the mostly stateless HTTP protocol.  Although
   cookies have many historical infelicities that degrade their security
   and privacy, the Cookie and Set-Cookie header fields are widely used
   on the Internet.  This document obsoletes RFC 2965.

Status of This Memo

   This is an Internet Standards Track document.

   This document is a product of the Internet Engineering Task Force
   (IETF).  It represents the consensus of the IETF community.  It has
   received public review and has been approved for publication by the
   Internet Engineering Steering Group (IESG).  Further information on
   Internet Standards is available in Section 2 of RFC 5741.

   Information about the current status of this document, any errata,
   and how to provide feedback on it may be obtained at
   http://www.rfc-editor.org/info/rfc6265.

Copyright Notice

   Copyright (c) 2011 IETF Trust and the persons identified as the
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

   1. Introduction ....................................................3
   2. Conventions .....................................................4
      2.1. Conformance Criteria .......................................4
      2.2. Syntax Notation ............................................5
      2.3. Terminology ................................................5
   3. Overview ........................................................6
      3.1. Examples ...................................................6
   4. Server Requirements .............................................8
      4.1. Set-Cookie .................................................8
           4.1.1. Syntax ..............................................8
           4.1.2. Semantics (Non-Normative) ..........................10
      4.2. Cookie ....................................................13
           4.2.1. Syntax .............................................13
           4.2.2. Semantics ..........................................13
   5. User Agent Requirements ........................................14
      5.1. Subcomponent Algorithms ...................................14
           5.1.1. Dates ..............................................14
           5.1.2. Canonicalized Host Names ...........................16
           5.1.3. Domain Matching ....................................16
           5.1.4. Paths and Path-Match ...............................16
      5.2. The Set-Cookie Header .....................................17
           5.2.1. The Expires Attribute ..............................19
           5.2.2. The Max-Age Attribute ..............................20
           5.2.3. The Domain Attribute ...............................20
           5.2.4. The Path Attribute .................................21
           5.2.5. The Secure Attribute ...............................21
           5.2.6. The HttpOnly Attribute .............................21
      5.3. Storage Model .............................................21
      5.4. The Cookie Header .........................................25
   6. Implementation Considerations ..................................27
      6.1. Limits ....................................................27
      6.2. Application Programming Interfaces ........................27
      6.3. IDNA Dependency and Migration .............................27
   7. Privacy Considerations .........................................28

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
