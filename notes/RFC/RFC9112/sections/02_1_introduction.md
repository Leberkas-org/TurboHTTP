---
title: "1.  Introduction"
rfc_number: 9112
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 1: Introduction — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, introduction]
---

## 1.  Introduction

1.  Introduction

   The Hypertext Transfer Protocol (HTTP) is a stateless application-
   level request/response protocol that uses extensible semantics and
   self-descriptive messages for flexible interaction with network-based
   hypertext information systems.  HTTP/1.1 is defined by:

   *  This document

   *  "HTTP Semantics" [HTTP]

   *  "HTTP Caching" [CACHING]

   This document specifies how HTTP semantics are conveyed using the
   HTTP/1.1 message syntax, framing, and connection management
   mechanisms.  Its goal is to define the complete set of requirements
   for HTTP/1.1 message parsers and message-forwarding intermediaries.

   This document obsoletes the portions of RFC 7230 related to HTTP/1.1
   messaging and connection management, with the changes being
   summarized in Appendix C.3.  The other parts of RFC 7230 are
   obsoleted by "HTTP Semantics" [HTTP].

## 1.1  Requirements Notation

   The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and
   "OPTIONAL" in this document are to be interpreted as described in
   BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all
   capitals, as shown here.

   Conformance criteria and considerations regarding error handling are
   defined in Section 2 of [HTTP].

## 1.2  Syntax Notation

   This specification uses the Augmented Backus-Naur Form (ABNF)
   notation of [RFC5234], extended with the notation for case-
   sensitivity in strings defined in [RFC7405].

   It also uses a list extension, defined in Section 5.6.1 of [HTTP],
   that allows for compact definition of comma-separated lists using a
   "#" operator (similar to how the "*" operator indicates repetition).
   Appendix A shows the collected grammar with all list operators
   expanded to standard ABNF notation.

   As a convention, ABNF rule names prefixed with "obs-" denote obsolete
   grammar rules that appear for historical reasons.

   The following core rules are included by reference, as defined in
   [RFC5234], Appendix B.1: ALPHA (letters), CR (carriage return), CRLF
   (CR LF), CTL (controls), DIGIT (decimal 0-9), DQUOTE (double quote),
   HEXDIG (hexadecimal 0-9/A-F/a-f), HTAB (horizontal tab), LF (line
   feed), OCTET (any 8-bit sequence of data), SP (space), and VCHAR (any
   visible [USASCII] character).

   The rules below are defined in [HTTP]:


```abnf
     BWS           = <BWS, see [HTTP], Section 5.6.3>
     OWS           = <OWS, see [HTTP], Section 5.6.3>
     RWS           = <RWS, see [HTTP], Section 5.6.3>
     absolute-path = <absolute-path, see [HTTP], Section 4.1>
     field-name    = <field-name, see [HTTP], Section 5.1>
     field-value   = <field-value, see [HTTP], Section 5.5>
     obs-text      = <obs-text, see [HTTP], Section 5.6.4>
     quoted-string = <quoted-string, see [HTTP], Section 5.6.4>
     token         = <token, see [HTTP], Section 5.6.2>
```

     transfer-coding =
                     <transfer-coding, see [HTTP], Section 10.1.4>

   The rules below are defined in [URI]:


```abnf
     absolute-URI  = <absolute-URI, see [URI], Section 4.3>
     authority     = <authority, see [URI], Section 3.2>
     uri-host      = <host, see [URI], Section 3.2.2>
     port          = <port, see [URI], Section 3.2.3>
     query         = <query, see [URI], Section 3.4>
```

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
