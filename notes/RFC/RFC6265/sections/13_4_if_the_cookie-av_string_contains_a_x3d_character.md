---
title: "4.  If the cookie-av string contains a %x3D ("=") character:"
rfc_number: 6265
rfc_section: "4"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 4: If the cookie-av string contains a %x3D ("=") character: — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, if_the_cookie-av_string_contains_a_x3d_character]
---

# 4.  If the cookie-av string contains a %x3D ("=") character:



          The (possibly empty) attribute-name string consists of the
          characters up to, but not including, the first %x3D ("=")
          character, and the (possibly empty) attribute-value string
          consists of the characters after the first %x3D ("=")
          character.

       Otherwise:

          The attribute-name string consists of the entire cookie-av
          string, and the attribute-value string is empty.

   5.  Remove any leading or trailing WSP characters from the attribute-
       name string and the attribute-value string.

   6.  Process the attribute-name and attribute-value according to the
       requirements in the following subsections.  (Notice that
       attributes with unrecognized attribute-names are ignored.)

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
