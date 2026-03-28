---
title: "1.  Introduction"
rfc_number: 9111
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc9111"
description: "Section 1: Introduction — RFC 9111 — HTTP Caching"
tags: [RFC9111, HTTP-caching, freshness, validation, Cache-Control, max-age, Expires, conditional-requests, Vary, introduction]
---

## 1.  Introduction

1.  Introduction

   The Hypertext Transfer Protocol (HTTP) is a stateless application-
   level request/response protocol that uses extensible semantics and
   self-descriptive messages for flexible interaction with network-based
   hypertext information systems.  It is typically used for distributed
   information systems, where the use of response caches can improve
   performance.  This document defines aspects of HTTP related to
   caching and reusing response messages.

   An HTTP "cache" is a local store of response messages and the
   subsystem that controls storage, retrieval, and deletion of messages
   in it.  A cache stores cacheable responses to reduce the response
   time and network bandwidth consumption on future equivalent requests.
> **MAY**: Any client or server MAY use a cache, though not when acting as a
   tunnel (Section 3.7 of [HTTP]).

   A "shared cache" is a cache that stores responses for reuse by more
   than one user; shared caches are usually (but not always) deployed as
   a part of an intermediary.  A "private cache", in contrast, is
   dedicated to a single user; often, they are deployed as a component
   of a user agent.

   The goal of HTTP caching is significantly improving performance by
   reusing a prior response message to satisfy a current request.  A
   cache considers a stored response "fresh", as defined in Section 4.2,
   if it can be reused without "validation" (checking with the origin
   server to see if the cached response remains valid for this request).
   A fresh response can therefore reduce both latency and network
   overhead each time the cache reuses it.  When a cached response is
   not fresh, it might still be reusable if validation can freshen it
   (Section 4.3) or if the origin is unavailable (Section 4.2.4).

   This document obsoletes RFC 7234, with the changes being summarized
   in Appendix B.

## 1.1  Requirements Notation

   The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and
   "OPTIONAL" in this document are to be interpreted as described in
   BCP 14 [RFC2119] [RFC8174] when, and only when, they appear in all
   capitals, as shown here.

   Section 2 of [HTTP] defines conformance criteria and contains
   considerations regarding error handling.

## 1.2  Syntax Notation

   This specification uses the Augmented Backus-Naur Form (ABNF)
   notation of [RFC5234], extended with the notation for case-
   sensitivity in strings defined in [RFC7405].

   It also uses a list extension, defined in Section 5.6.1 of [HTTP],
   that allows for compact definition of comma-separated lists using a
   "#" operator (similar to how the "*" operator indicates repetition).
   Appendix A shows the collected grammar with all list operators
   expanded to standard ABNF notation.

### 1.2.1  Imported Rules

   The following core rule is included by reference, as defined in
   [RFC5234], Appendix B.1: DIGIT (decimal 0-9).

   [HTTP] defines the following rules:


```abnf
     HTTP-date     = <HTTP-date, see [HTTP], Section 5.6.7>
     OWS           = <OWS, see [HTTP], Section 5.6.3>
     field-name    = <field-name, see [HTTP], Section 5.1>
     quoted-string = <quoted-string, see [HTTP], Section 5.6.4>
     token         = <token, see [HTTP], Section 5.6.2>
```


### 1.2.2  Delta Seconds

   The delta-seconds rule specifies a non-negative integer, representing
   time in seconds.


```abnf
     delta-seconds  = 1*DIGIT
```


   A recipient parsing a delta-seconds value and converting it to binary
   form ought to use an arithmetic type of at least 31 bits of non-
   negative integer range.  If a cache receives a delta-seconds value
   greater than the greatest integer it can represent, or if any of its
> **MUST**: subsequent calculations overflows, the cache MUST consider the value
   to be 2147483648 (2^31) or the greatest positive integer it can
   conveniently represent.

      |  *Note:* The value 2147483648 is here for historical reasons,
      |  represents infinity (over 68 years), and does not need to be
      |  stored in binary form; an implementation could produce it as a
      |  string if any overflow occurs, even if the calculations are
      |  performed with an arithmetic type incapable of directly
      |  representing that number.  What matters here is that an
      |  overflow be detected and not treated as a negative value in
      |  later calculations.

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
