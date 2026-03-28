---
title: "4.1.  URI References"
rfc_number: 9110
rfc_section: "4.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 4.1: URI References — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, uri_references]
---

## 4.1.  URI References

4.  Identifiers in HTTP

   Uniform Resource Identifiers (URIs) [URI] are used throughout HTTP as
   the means for identifying resources (Section 3.1).

## 4.1  URI References

   URI references are used to target requests, indicate redirects, and
   define relationships.

   The definitions of "URI-reference", "absolute-URI", "relative-part",
   "authority", "port", "host", "path-abempty", "segment", and "query"
   are adopted from the URI generic syntax.  An "absolute-path" rule is
   defined for protocol elements that can contain a non-empty path
   component.  (This rule differs slightly from the path-abempty rule of
   RFC 3986, which allows for an empty path, and path-absolute rule,
   which does not allow paths that begin with "//".)  A "partial-URI"
   rule is defined for protocol elements that can contain a relative URI
   but not a fragment component.


```abnf
     URI-reference = <URI-reference, see [URI], Section 4.1>
     absolute-URI  = <absolute-URI, see [URI], Section 4.3>
     relative-part = <relative-part, see [URI], Section 4.2>
     authority     = <authority, see [URI], Section 3.2>
     uri-host      = <host, see [URI], Section 3.2.2>
     port          = <port, see [URI], Section 3.2.3>
     path-abempty  = <path-abempty, see [URI], Section 3.3>
     segment       = <segment, see [URI], Section 3.3>
     query         = <query, see [URI], Section 3.4>

     absolute-path = 1*( "/" segment )
     partial-URI   = relative-part [ "?" query ]
```


   Each protocol element in HTTP that allows a URI reference will
   indicate in its ABNF production whether the element allows any form
   of reference (URI-reference), only a URI in absolute form (absolute-
   URI), only the path and optional query components (partial-URI), or
   some combination of the above.  Unless otherwise indicated, URI
   references are parsed relative to the target URI (Section 7.1).

   It is RECOMMENDED that all senders and recipients support, at a
   minimum, URIs with lengths of 8000 octets in protocol elements.  Note
   that this implies some structures and on-wire representations (for
   example, the request line in HTTP/1.1) will necessarily be larger in
   some cases.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
