---
title: "12.  Insert the newly created cookie into the cookie store."
rfc_number: 6265
rfc_section: "12"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 12: Insert the newly created cookie into the cookie store. — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, insert_the_newly_created_cookie_into_the_cookie_st]
---

# 12.  Insert the newly created cookie into the cookie store.


   A cookie is "expired" if the cookie has an expiry date in the past.

> **MUST**: The user agent MUST evict all expired cookies from the cookie store
   if, at any time, an expired cookie exists in the cookie store.

> **MAY**: At any time, the user agent MAY "remove excess cookies" from the
   cookie store if the number of cookies sharing a domain field exceeds
   some implementation-defined upper bound (such as 50 cookies).

> **MAY**: At any time, the user agent MAY "remove excess cookies" from the
   cookie store if the cookie store exceeds some predetermined upper
   bound (such as 3000 cookies).

   When the user agent removes excess cookies from the cookie store, the
> **MUST**: user agent MUST evict cookies in the following priority order:

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
