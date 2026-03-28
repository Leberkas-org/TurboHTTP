---
title: "2.  Conventions"
rfc_number: 6265
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 2: Conventions — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, conventions]
---

# 2.  Conventions


## 2.1.  Conformance Criteria

> **MUST**: The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this
   document are to be interpreted as described in [RFC2119].





> **MUST**: Requirements phrased in the imperative as part of algorithms (such as
   "strip any leading space characters" or "return false and abort these
   steps") are to be interpreted with the meaning of the key word
   ("MUST", "SHOULD", "MAY", etc.) used in introducing the algorithm.

   Conformance requirements phrased as algorithms or specific steps can
   be implemented in any manner, so long as the end result is
   equivalent.  In particular, the algorithms defined in this
   specification are intended to be easy to understand and are not
   intended to be performant.

## 2.2.  Syntax Notation

   This specification uses the Augmented Backus-Naur Form (ABNF)
   notation of [RFC5234].

   The following core rules are included by reference, as defined in
   [RFC5234], Appendix B.1: ALPHA (letters), CR (carriage return), CRLF
   (CR LF), CTLs (controls), DIGIT (decimal 0-9), DQUOTE (double quote),
   HEXDIG (hexadecimal 0-9/A-F/a-f), LF (line feed), NUL (null octet),
   OCTET (any 8-bit sequence of data except NUL), SP (space), HTAB
   (horizontal tab), CHAR (any [USASCII] character), VCHAR (any visible
   [USASCII] character), and WSP (whitespace).

   The OWS (optional whitespace) rule is used where zero or more linear
> **MAY**: whitespace characters MAY appear:


```abnf
   OWS            = *( [ obs-fold ] WSP )
                    ; "optional" whitespace
   obs-fold       = CRLF
```


> **SHOULD**: OWS SHOULD either not be produced or be produced as a single SP
   character.

## 2.3.  Terminology

   The terms user agent, client, server, proxy, and origin server have
   the same meaning as in the HTTP/1.1 specification ([RFC2616], Section
   1.3).

   The request-host is the name of the host, as known by the user agent,
   to which the user agent is sending an HTTP request or from which it
   is receiving an HTTP response (i.e., the name of the host to which it
   sent the corresponding HTTP request).

   The term request-uri is defined in Section 5.1.2 of [RFC2616].





   Two sequences of octets are said to case-insensitively match each
   other if and only if they are equivalent under the i;ascii-casemap
   collation defined in [RFC4790].

   The term string means a sequence of non-NUL octets.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
