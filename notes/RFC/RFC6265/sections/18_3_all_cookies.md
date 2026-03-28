---
title: "3.  All cookies."
rfc_number: 6265
rfc_section: "3"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 3: All cookies. — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, all_cookies]
---

# 3.  All cookies.


> **MUST**: If two cookies have the same removal priority, the user agent MUST
   evict the cookie with the earliest last-access date first.



   When "the current session is over" (as defined by the user agent),
> **MUST**: the user agent MUST remove from the cookie store all cookies with the
   persistent-flag set to false.

## 5.4.  The Cookie Header

   The user agent includes stored cookies in the Cookie HTTP request
   header.

> **MUST**: When the user agent generates an HTTP request, the user agent MUST
   NOT attach more than one Cookie header field.

> **MAY**: A user agent MAY omit the Cookie header in its entirety.  For
   example, the user agent might wish to block sending cookies during
   "third-party" requests from setting cookies (see Section 7.1).

   If the user agent does attach a Cookie header field to an HTTP
> **MUST**: request, the user agent MUST send the cookie-string (defined below)
   as the value of the header field.

> **MUST**: The user agent MUST use an algorithm equivalent to the following
   algorithm to compute the "cookie-string" from a cookie store and a
   request-uri:

   1.  Let cookie-list be the set of cookies from the cookie store that
       meets all of the following requirements:

       *  Either:

             The cookie's host-only-flag is true and the canonicalized
             request-host is identical to the cookie's domain.

          Or:

             The cookie's host-only-flag is false and the canonicalized
             request-host domain-matches the cookie's domain.

       *  The request-uri's path path-matches the cookie's path.

       *  If the cookie's secure-only-flag is true, then the request-
          uri's scheme must denote a "secure" protocol (as defined by
          the user agent).

             NOTE: The notion of a "secure" protocol is not defined by
             this document.  Typically, user agents consider a protocol
             secure if the protocol makes use of transport-layer





             security, such as SSL or TLS.  For example, most user
             agents consider "https" to be a scheme that denotes a
             secure protocol.

       *  If the cookie's http-only-flag is true, then exclude the
          cookie if the cookie-string is being generated for a "non-
          HTTP" API (as defined by the user agent).

> **SHOULD**: 2.  The user agent SHOULD sort the cookie-list in the following
       order:

       *  Cookies with longer paths are listed before cookies with
          shorter paths.

       *  Among cookies that have equal-length path fields, cookies with
          earlier creation-times are listed before cookies with later
          creation-times.

       NOTE: Not all user agents sort the cookie-list in this order, but
       this order reflects common practice when this document was
       written, and, historically, there have been servers that
       (erroneously) depended on this order.

   3.  Update the last-access-time of each cookie in the cookie-list to
       the current date and time.

   4.  Serialize the cookie-list into a cookie-string by processing each
       cookie in the cookie-list in order:

       1.  Output the cookie's name, the %x3D ("=") character, and the
           cookie's value.

       2.  If there is an unprocessed cookie in the cookie-list, output
           the characters %x3B and %x20 ("; ").

   NOTE: Despite its name, the cookie-string is actually a sequence of
   octets, not a sequence of characters.  To convert the cookie-string
   (or components thereof) into a sequence of characters (e.g., for
   presentation to the user), the user agent might wish to try using the
   UTF-8 character encoding [RFC3629] to decode the octet sequence.
   This decoding might fail, however, because not every sequence of
   octets is valid UTF-8.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
