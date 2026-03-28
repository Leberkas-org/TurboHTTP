---
title: "10.7.  Expires"
rfc_number: 1945
rfc_section: "10.7"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.7: Expires — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, expires]
---

# 10.7.  Expires

## 10.7  Expires

   The Expires entity-header field gives the date/time after which the
   entity should be considered stale. This allows information providers
   to suggest the volatility of the resource, or a date after which the
   information may no longer be valid. Applications must not cache this
   entity beyond the date given. The presence of an Expires field does
   not imply that the original resource will change or cease to exist
   at, before, or after that time. However, information providers that
   know or even suspect that a resource will change by a certain date
   should include an Expires header with that date. The format is an
   absolute date and time as defined by HTTP-date in Section 3.3.


```abnf
       Expires        = "Expires" ":" HTTP-date
```


   An example of its use is

       Expires: Thu, 01 Dec 1994 16:00:00 GMT

   If the date given is equal to or earlier than the value of the Date
   header, the recipient must not cache the enclosed entity. If a
   resource is dynamic by nature, as is the case with many data-
   producing processes, entities from that resource should be given an
   appropriate Expires value which reflects that dynamism.

   The Expires field cannot be used to force a user agent to refresh its
   display or reload a resource; its semantics apply only to caching
   mechanisms, and such mechanisms need only check a resource's
   expiration status when a new request for that resource is initiated.

   User agents often have history mechanisms, such as "Back" buttons and
   history lists, which can be used to redisplay an entity retrieved
   earlier in a session. By default, the Expires field does not apply to
   history mechanisms. If the entity is still in storage, a history
   mechanism should display it even if the entity has expired, unless
   the user has specifically configured the agent to refresh expired
   history documents.

      Note: Applications are encouraged to be tolerant of bad or
      misinformed implementations of the Expires header. A value of zero
      (0) or an invalid date format should be considered equivalent to
      an "expires immediately." Although these values are not legitimate
      for HTTP/1.0, a robust implementation is always desirable.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
