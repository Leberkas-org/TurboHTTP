---
title: "6.  If the domain-attribute is non-empty:"
rfc_number: 6265
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 6: If the domain-attribute is non-empty: — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, if_the_domain-attribute_is_non-empty]
---

# 6.  If the domain-attribute is non-empty:



           If the canonicalized request-host does not domain-match the
           domain-attribute:

              Ignore the cookie entirely and abort these steps.

           Otherwise:

              Set the cookie's host-only-flag to false.

              Set the cookie's domain to the domain-attribute.

        Otherwise:

           Set the cookie's host-only-flag to true.

           Set the cookie's domain to the canonicalized request-host.

   7.   If the cookie-attribute-list contains an attribute with an
        attribute-name of "Path", set the cookie's path to attribute-
        value of the last attribute in the cookie-attribute-list with an
        attribute-name of "Path".  Otherwise, set the cookie's path to
        the default-path of the request-uri.

   8.   If the cookie-attribute-list contains an attribute with an
        attribute-name of "Secure", set the cookie's secure-only-flag to
        true.  Otherwise, set the cookie's secure-only-flag to false.

   9.   If the cookie-attribute-list contains an attribute with an
        attribute-name of "HttpOnly", set the cookie's http-only-flag to
        true.  Otherwise, set the cookie's http-only-flag to false.





   10.  If the cookie was received from a "non-HTTP" API and the
        cookie's http-only-flag is set, abort these steps and ignore the
        cookie entirely.

   11.  If the cookie store contains a cookie with the same name,
        domain, and path as the newly created cookie:

        1.  Let old-cookie be the existing cookie with the same name,
            domain, and path as the newly created cookie.  (Notice that
            this algorithm maintains the invariant that there is at most
            one such cookie.)

        2.  If the newly created cookie was received from a "non-HTTP"
            API and the old-cookie's http-only-flag is set, abort these
            steps and ignore the newly created cookie entirely.

        3.  Update the creation-time of the newly created cookie to
            match the creation-time of the old-cookie.

        4.  Remove the old-cookie from the cookie store.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
