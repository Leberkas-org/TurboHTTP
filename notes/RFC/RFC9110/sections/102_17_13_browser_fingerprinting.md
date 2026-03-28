---
title: "17.13.  Browser Fingerprinting"
rfc_number: 9110
rfc_section: "17.13"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.13: Browser Fingerprinting — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, browser_fingerprinting]
---

## 17.13.  Browser Fingerprinting

## 17.13  Browser Fingerprinting

   Browser fingerprinting is a set of techniques for identifying a
   specific user agent over time through its unique set of
   characteristics.  These characteristics might include information
   related to how it uses the underlying transport protocol, feature
   capabilities, and scripting environment, though of particular
   interest here is the set of unique characteristics that might be
   communicated via HTTP.  Fingerprinting is considered a privacy
   concern because it enables tracking of a user agent's behavior over
   time ([Bujlow]) without the corresponding controls that the user
   might have over other forms of data collection (e.g., cookies).  Many
   general-purpose user agents (i.e., Web browsers) have taken steps to
   reduce their fingerprints.

   There are a number of request header fields that might reveal
   information to servers that is sufficiently unique to enable
   fingerprinting.  The From header field is the most obvious, though it
   is expected that From will only be sent when self-identification is
   desired by the user.  Likewise, Cookie header fields are deliberately
   designed to enable re-identification, so fingerprinting concerns only
   apply to situations where cookies are disabled or restricted by the
   user agent's configuration.

   The User-Agent header field might contain enough information to
   uniquely identify a specific device, usually when combined with other
   characteristics, particularly if the user agent sends excessive
   details about the user's system or extensions.  However, the source
   of unique information that is least expected by users is proactive
   negotiation (Section 12.1), including the Accept, Accept-Charset,
   Accept-Encoding, and Accept-Language header fields.

   In addition to the fingerprinting concern, detailed use of the
   Accept-Language header field can reveal information the user might
   consider to be of a private nature.  For example, understanding a
   given language set might be strongly correlated to membership in a
   particular ethnic group.  An approach that limits such loss of
   privacy would be for a user agent to omit the sending of Accept-
   Language except for sites that have been explicitly permitted,
   perhaps via interaction after detecting a Vary header field that
   indicates language negotiation might be useful.

   In environments where proxies are used to enhance privacy, user
   agents ought to be conservative in sending proactive negotiation
   header fields.  General-purpose user agents that provide a high
   degree of header field configurability ought to inform users about
   the loss of privacy that might result if too much detail is provided.
   As an extreme privacy measure, proxies could filter the proactive
   negotiation header fields in relayed requests.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
