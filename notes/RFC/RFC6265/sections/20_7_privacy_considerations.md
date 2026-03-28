---
title: "7.  Privacy Considerations"
rfc_number: 6265
rfc_section: "7"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 7: Privacy Considerations — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, privacy_considerations]
---

# 7.  Privacy Considerations


   Cookies are often criticized for letting servers track users.  For
   example, a number of "web analytics" companies use cookies to
   recognize when a user returns to a web site or visits another web
   site.  Although cookies are not the only mechanism servers can use to
   track users across HTTP requests, cookies facilitate tracking because
   they are persistent across user agent sessions and can be shared
   between hosts.

## 7.1.  Third-Party Cookies

   Particularly worrisome are so-called "third-party" cookies.  In
   rendering an HTML document, a user agent often requests resources
   from other servers (such as advertising networks).  These third-party
   servers can use cookies to track the user even if the user never
   visits the server directly.  For example, if a user visits a site
   that contains content from a third party and then later visits
   another site that contains content from the same third party, the
   third party can track the user between the two sites.

   Some user agents restrict how third-party cookies behave.  For
   example, some of these user agents refuse to send the Cookie header
   in third-party requests.  Others refuse to process the Set-Cookie
   header in responses to third-party requests.  User agents vary widely
   in their third-party cookie policies.  This document grants user
   agents wide latitude to experiment with third-party cookie policies
   that balance the privacy and compatibility needs of their users.
   However, this document does not endorse any particular third-party
   cookie policy.

   Third-party cookie blocking policies are often ineffective at
   achieving their privacy goals if servers attempt to work around their
   restrictions to track users.  In particular, two collaborating
   servers can often track users without using cookies at all by
   injecting identifying information into dynamic URLs.

## 7.2.  User Controls

> **SHOULD**: User agents SHOULD provide users with a mechanism for managing the
   cookies stored in the cookie store.  For example, a user agent might
   let users delete all cookies received during a specified time period





   or all the cookies related to a particular domain.  In addition, many
   user agents include a user interface element that lets users examine
   the cookies stored in their cookie store.

> **SHOULD**: User agents SHOULD provide users with a mechanism for disabling
   cookies.  When cookies are disabled, the user agent MUST NOT include
> **MUST NOT**: a Cookie header in outbound HTTP requests and the user agent MUST NOT
   process Set-Cookie headers in inbound HTTP responses.

   Some user agents provide users the option of preventing persistent
   storage of cookies across sessions.  When configured thusly, user
> **MUST**: agents MUST treat all received cookies as if the persistent-flag were
   set to false.  Some popular user agents expose this functionality via
   "private browsing" mode [Aggarwal2010].

   Some user agents provide users with the ability to approve individual
   writes to the cookie store.  In many common usage scenarios, these
   controls generate a large number of prompts.  However, some privacy-
   conscious users find these controls useful nonetheless.

## 7.3.  Expiration Dates

   Although servers can set the expiration date for cookies to the
   distant future, most user agents do not actually retain cookies for
   multiple decades.  Rather than choosing gratuitously long expiration
> **SHOULD**: periods, servers SHOULD promote user privacy by selecting reasonable
   cookie expiration periods based on the purpose of the cookie.  For
   example, a typical session identifier might reasonably be set to
   expire in two weeks.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
