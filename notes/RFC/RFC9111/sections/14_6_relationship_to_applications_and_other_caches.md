---
title: "6.  Relationship to Applications and Other Caches"
rfc_number: 9111
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc9111"
description: "Section 6: Relationship to Applications and Other Caches — RFC 9111 — HTTP Caching"
tags: [RFC9111, HTTP-caching, freshness, validation, Cache-Control, max-age, Expires, conditional-requests, Vary, relationship_to_applications_and_other_caches]
---

## 6.  Relationship to Applications and Other Caches

6.  Relationship to Applications and Other Caches

   Applications using HTTP often specify additional forms of caching.
   For example, Web browsers often have history mechanisms such as
   "Back" buttons that can be used to redisplay a representation
   retrieved earlier in a session.

   Likewise, some Web browsers implement caching of images and other
   assets within a page view; they may or may not honor HTTP caching
   semantics.

   The requirements in this specification do not necessarily apply to
   how applications use data after it is retrieved from an HTTP cache.
   For example, a history mechanism can display a previous
   representation even if it has expired, and an application can use
   cached data in other ways beyond its freshness lifetime.

   This specification does not prohibit the application from taking HTTP
   caching into account; for example, a history mechanism might tell the
   user that a view is stale, or it might honor cache directives (e.g.,
   Cache-Control: no-store).

   However, when an application caches data and does not make this
   apparent to or easily controllable by the user, it is strongly
   encouraged to define its operation with respect to HTTP cache
   directives so as not to surprise authors who expect caching semantics
   to be honored.  For example, while it might be reasonable to define
   an application cache "above" HTTP that allows a response containing
   Cache-Control: no-store to be reused for requests that are directly
   related to the request that fetched it (such as those created during
   the same page load), it would likely be surprising and confusing to
   users and authors if it were allowed to be reused for requests
   unrelated in any way to the one from which it was obtained.

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
