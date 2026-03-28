---
title: "1.  Introduction"
rfc_number: 6265
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 1: Introduction — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, introduction]
---

# 1.  Introduction


   This document defines the HTTP Cookie and Set-Cookie header fields.
   Using the Set-Cookie header field, an HTTP server can pass name/value
   pairs and associated metadata (called cookies) to a user agent.  When
   the user agent makes subsequent requests to the server, the user
   agent uses the metadata and other information to determine whether to
   return the name/value pairs in the Cookie header.

   Although simple on their surface, cookies have a number of
   complexities.  For example, the server indicates a scope for each
   cookie when sending it to the user agent.  The scope indicates the
   maximum amount of time in which the user agent should return the
   cookie, the servers to which the user agent should return the cookie,
   and the URI schemes for which the cookie is applicable.

   For historical reasons, cookies contain a number of security and
   privacy infelicities.  For example, a server can indicate that a
   given cookie is intended for "secure" connections, but the Secure
   attribute does not provide integrity in the presence of an active
   network attacker.  Similarly, cookies for a given host are shared
   across all the ports on that host, even though the usual "same-origin
   policy" used by web browsers isolates content retrieved via different
   ports.

   There are two audiences for this specification: developers of cookie-
   generating servers and developers of cookie-consuming user agents.



> **SHOULD**: To maximize interoperability with user agents, servers SHOULD limit
   themselves to the well-behaved profile defined in Section 4 when
   generating cookies.

> **MUST**: User agents MUST implement the more liberal processing rules defined
   in Section 5, in order to maximize interoperability with existing
   servers that do not conform to the well-behaved profile defined in
   Section 4.

   This document specifies the syntax and semantics of these headers as
   they are actually used on the Internet.  In particular, this document
   does not create new syntax or semantics beyond those in use today.
   The recommendations for cookie generation provided in Section 4
   represent a preferred subset of current server behavior, and even the
   more liberal cookie processing algorithm provided in Section 5 does
   not recommend all of the syntactic and semantic variations in use
   today.  Where some existing software differs from the recommended
   protocol in significant ways, the document contains a note explaining
   the difference.

   Prior to this document, there were at least three descriptions of
   cookies: the so-called "Netscape cookie specification" [Netscape],
   RFC 2109 [RFC2109], and RFC 2965 [RFC2965].  However, none of these
   documents describe how the Cookie and Set-Cookie headers are actually
   used on the Internet (see [Kri2001] for historical context).  In
   relation to previous IETF specifications of HTTP state management
   mechanisms, this document requests the following actions:

   1.  Change the status of [RFC2109] to Historic (it has already been
       obsoleted by [RFC2965]).

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
