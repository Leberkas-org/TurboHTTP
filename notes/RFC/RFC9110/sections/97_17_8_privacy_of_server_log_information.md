---
title: "17.8.  Privacy of Server Log Information"
rfc_number: 9110
rfc_section: "17.8"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.8: Privacy of Server Log Information — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, privacy_of_server_log_information]
---

## 17.8.  Privacy of Server Log Information

## 17.8  Privacy of Server Log Information

   A server is in the position to save personal data about a user's
   requests over time, which might identify their reading patterns or
   subjects of interest.  In particular, log information gathered at an
   intermediary often contains a history of user agent interaction,
   across a multitude of sites, that can be traced to individual users.

   HTTP log information is confidential in nature; its handling is often
   constrained by laws and regulations.  Log information needs to be
   securely stored and appropriate guidelines followed for its analysis.
   Anonymization of personal information within individual entries
   helps, but it is generally not sufficient to prevent real log traces
   from being re-identified based on correlation with other access
   characteristics.  As such, access traces that are keyed to a specific
   client are unsafe to publish even if the key is pseudonymous.

   To minimize the risk of theft or accidental publication, log
   information ought to be purged of personally identifiable
   information, including user identifiers, IP addresses, and user-
   provided query parameters, as soon as that information is no longer
   necessary to support operational needs for security, auditing, or
   fraud control.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
