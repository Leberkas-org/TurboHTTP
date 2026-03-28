---
title: "3.5.  User Agents"
rfc_number: 9110
rfc_section: "3.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.5: User Agents — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, user_agents]
---

## 3.5.  User Agents

## 3.5  User Agents

   The term "user agent" refers to any of the various client programs
   that initiate a request.

   The most familiar form of user agent is the general-purpose Web
   browser, but that's only a small percentage of implementations.
   Other common user agents include spiders (web-traversing robots),
   command-line tools, billboard screens, household appliances, scales,
   light bulbs, firmware update scripts, mobile apps, and communication
   devices in a multitude of shapes and sizes.

   Being a user agent does not imply that there is a human user directly
   interacting with the software agent at the time of a request.  In
   many cases, a user agent is installed or configured to run in the
   background and save its results for later inspection (or save only a
   subset of those results that might be interesting or erroneous).
   Spiders, for example, are typically given a start URI and configured
   to follow certain behavior while crawling the Web as a hypertext
   graph.

   Many user agents cannot, or choose not to, make interactive
   suggestions to their user or provide adequate warning for security or
   privacy concerns.  In the few cases where this specification requires
   reporting of errors to the user, it is acceptable for such reporting
   to only be observable in an error console or log file.  Likewise,
   requirements that an automated action be confirmed by the user before
   proceeding might be met via advance configuration choices, run-time
   options, or simple avoidance of the unsafe action; confirmation does
   not imply any specific user interface or interruption of normal
   processing if the user has already made that choice.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
