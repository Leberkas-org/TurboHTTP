---
title: "5.  Abort these steps and fail to parse the cookie-date if:"
rfc_number: 6265
rfc_section: "5"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 5: Abort these steps and fail to parse the cookie-date if: — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, abort_these_steps_and_fail_to_parse_the_cookie-dat]
---

# 5.  Abort these steps and fail to parse the cookie-date if:


       *  at least one of the found-day-of-month, found-month, found-
          year, or found-time flags is not set,

       *  the day-of-month-value is less than 1 or greater than 31,

       *  the year-value is less than 1601,

       *  the hour-value is greater than 23,

       *  the minute-value is greater than 59, or

       *  the second-value is greater than 59.

       (Note that leap seconds cannot be represented in this syntax.)




   6.  Let the parsed-cookie-date be the date whose day-of-month, month,
       year, hour, minute, and second (in UTC) are the day-of-month-
       value, the month-value, the year-value, the hour-value, the
       minute-value, and the second-value, respectively.  If no such
       date exists, abort these steps and fail to parse the cookie-date.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
