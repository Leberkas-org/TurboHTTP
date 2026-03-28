---
title: "1.  If the set-cookie-string contains a %x3B (";") character:"
rfc_number: 6265
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 1: If the set-cookie-string contains a %x3B (";") character: — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, if_the_set-cookie-string_contains_a_x3b_character]
---

# 1.  If the set-cookie-string contains a %x3B (";") character:



          The name-value-pair string consists of the characters up to,
          but not including, the first %x3B (";"), and the unparsed-
          attributes consist of the remainder of the set-cookie-string
          (including the %x3B (";") in question).

       Otherwise:

          The name-value-pair string consists of all the characters
          contained in the set-cookie-string, and the unparsed-
          attributes is the empty string.

   2.  If the name-value-pair string lacks a %x3D ("=") character,
       ignore the set-cookie-string entirely.

   3.  The (possibly empty) name string consists of the characters up
       to, but not including, the first %x3D ("=") character, and the
       (possibly empty) value string consists of the characters after
       the first %x3D ("=") character.

   4.  Remove any leading or trailing WSP characters from the name
       string and the value string.

   5.  If the name string is empty, ignore the set-cookie-string
       entirely.

   6.  The cookie-name is the name string, and the cookie-value is the
       value string.

> **MUST**: The user agent MUST use an algorithm equivalent to the following
   algorithm to parse the unparsed-attributes:

   1.  If the unparsed-attributes string is empty, skip the rest of
       these steps.

   2.  Discard the first character of the unparsed-attributes (which
       will be a %x3B (";") character).

   3.  If the remaining unparsed-attributes contains a %x3B (";")
       character:

          Consume the characters of the unparsed-attributes up to, but
          not including, the first %x3B (";") character.




       Otherwise:

          Consume the remainder of the unparsed-attributes.

       Let the cookie-av string be the characters consumed in this step.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
