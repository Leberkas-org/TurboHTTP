---
title: "3.  Overview"
rfc_number: 6265
rfc_section: "3"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 3: Overview — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, overview]
---

# 3.  Overview


   This section outlines a way for an origin server to send state
   information to a user agent and for the user agent to return the
   state information to the origin server.

   To store state, the origin server includes a Set-Cookie header in an
   HTTP response.  In subsequent requests, the user agent returns a
   Cookie request header to the origin server.  The Cookie header
   contains cookies the user agent received in previous Set-Cookie
   headers.  The origin server is free to ignore the Cookie header or
   use its contents for an application-defined purpose.

> **MAY**: Origin servers MAY send a Set-Cookie response header with any
   response.  User agents MAY ignore Set-Cookie headers contained in
> **MUST**: responses with 100-level status codes but MUST process Set-Cookie
   headers contained in other responses (including responses with 400-
   and 500-level status codes).  An origin server can include multiple
   Set-Cookie header fields in a single response.  The presence of a
   Cookie or a Set-Cookie header field does not preclude HTTP caches
   from storing and reusing a response.

> **SHOULD NOT**: Origin servers SHOULD NOT fold multiple Set-Cookie header fields into
   a single header field.  The usual mechanism for folding HTTP headers
   fields (i.e., as defined in [RFC2616]) might change the semantics of
   the Set-Cookie header field because the %x2C (",") character is used
   by Set-Cookie in a way that conflicts with such folding.

## 3.1.  Examples

   Using the Set-Cookie header, a server can send the user agent a short
   string in an HTTP response that the user agent will return in future
   HTTP requests that are within the scope of the cookie.  For example,
   the server can send the user agent a "session identifier" named SID
   with the value 31d4d96e407aad42.  The user agent then returns the
   session identifier in subsequent requests.









   == Server -> User Agent ==

   Set-Cookie: SID=31d4d96e407aad42

   == User Agent -> Server ==

   Cookie: SID=31d4d96e407aad42

   The server can alter the default scope of the cookie using the Path
   and Domain attributes.  For example, the server can instruct the user
   agent to return the cookie to every path and every subdomain of
   example.com.

   == Server -> User Agent ==

   Set-Cookie: SID=31d4d96e407aad42; Path=/; Domain=example.com

   == User Agent -> Server ==

   Cookie: SID=31d4d96e407aad42

   As shown in the next example, the server can store multiple cookies
   at the user agent.  For example, the server can store a session
   identifier as well as the user's preferred language by returning two
   Set-Cookie header fields.  Notice that the server uses the Secure and
   HttpOnly attributes to provide additional security protections for
   the more sensitive session identifier (see Section 4.1.2.)

   == Server -> User Agent ==

   Set-Cookie: SID=31d4d96e407aad42; Path=/; Secure; HttpOnly
   Set-Cookie: lang=en-US; Path=/; Domain=example.com

   == User Agent -> Server ==

   Cookie: SID=31d4d96e407aad42; lang=en-US

   Notice that the Cookie header above contains two cookies, one named
   SID and one named lang.  If the server wishes the user agent to
   persist the cookie over multiple "sessions" (e.g., user agent
   restarts), the server can specify an expiration date in the Expires
   attribute.  Note that the user agent might delete the cookie before
   the expiration date if the user agent's cookie store exceeds its
   quota or if the user manually deletes the server's cookie.







   == Server -> User Agent ==

   Set-Cookie: lang=en-US; Expires=Wed, 09 Jun 2021 10:18:14 GMT

   == User Agent -> Server ==

   Cookie: SID=31d4d96e407aad42; lang=en-US

   Finally, to remove a cookie, the server returns a Set-Cookie header
   with an expiration date in the past.  The server will be successful
   in removing the cookie only if the Path and the Domain attribute in
   the Set-Cookie header match the values used when the cookie was
   created.

   == Server -> User Agent ==

   Set-Cookie: lang=; Expires=Sun, 06 Nov 1994 08:49:37 GMT

   == User Agent -> Server ==

   Cookie: SID=31d4d96e407aad42

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
