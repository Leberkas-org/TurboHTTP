---
title: "17.14.  Validator Retention"
rfc_number: 9110
rfc_section: "17.14"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.14: Validator Retention — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, validator_retention]
---

## 17.14.  Validator Retention

## 17.14  Validator Retention

   The validators defined by this specification are not intended to
   ensure the validity of a representation, guard against malicious
   changes, or detect on-path attacks.  At best, they enable more
   efficient cache updates and optimistic concurrent writes when all
   participants are behaving nicely.  At worst, the conditions will fail
   and the client will receive a response that is no more harmful than
   an HTTP exchange without conditional requests.

   An entity tag can be abused in ways that create privacy risks.  For
   example, a site might deliberately construct a semantically invalid
   entity tag that is unique to the user or user agent, send it in a
   cacheable response with a long freshness time, and then read that
   entity tag in later conditional requests as a means of re-identifying
   that user or user agent.  Such an identifying tag would become a
   persistent identifier for as long as the user agent retained the
   original cache entry.  User agents that cache representations ought
   to ensure that the cache is cleared or replaced whenever the user
   performs privacy-maintaining actions, such as clearing stored cookies
   or changing to a private browsing mode.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
