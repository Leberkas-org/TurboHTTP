---
title: 5.  User Agent Requirements
rfc_number: 6265
rfc_section: "5"
source_url: https://www.rfc-editor.org/rfc/rfc6265
description: "Section 5: User Agent Requirements — RFC 6265 — HTTP State Management (Cookies)"
tags:
  - RFC6265
  - cookies
  - state-management
  - Set-Cookie
  - domain-matching
  - path-matching
  - SameSite
  - HttpOnly
  - user_agent_requirements
---

# 5.  User Agent Requirements



   This section specifies the Cookie and Set-Cookie headers in
   sufficient detail that a user agent implementing these requirements
   precisely can interoperate with existing servers (even those that do
   not conform to the well-behaved profile described in Section 4).

   A user agent could enforce more restrictions than those specified
   herein (e.g., for the sake of improved security); however,
   experiments have shown that such strictness reduces the likelihood
   that a user agent will be able to interoperate with existing servers.

## 5.1.  Subcomponent Algorithms

   This section defines some algorithms used by user agents to process
   specific subcomponents of the Cookie and Set-Cookie headers.

### 5.1.1.  Dates

> **MUST**: The user agent MUST use an algorithm equivalent to the following
   algorithm to parse a cookie-date.  Note that the various boolean
   flags defined as a part of the algorithm (i.e., found-time, found-
   day-of-month, found-month, found-year) are initially "not set".

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
