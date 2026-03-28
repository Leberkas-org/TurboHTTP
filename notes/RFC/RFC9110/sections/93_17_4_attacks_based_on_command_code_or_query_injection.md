---
title: "17.4.  Attacks Based on Command, Code, or Query Injection"
rfc_number: 9110
rfc_section: "17.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.4: Attacks Based on Command, Code, or Query Injection — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, attacks_based_on_command_code_or_query_injection]
---

## 17.4.  Attacks Based on Command, Code, or Query Injection

## 17.4  Attacks Based on Command, Code, or Query Injection

   Origin servers often use parameters within the URI as a means of
   identifying system services, selecting database entries, or choosing
   a data source.  However, data received in a request cannot be
   trusted.  An attacker could construct any of the request data
   elements (method, target URI, header fields, or content) to contain
   data that might be misinterpreted as a command, code, or query when
   passed through a command invocation, language interpreter, or
   database interface.

   For example, SQL injection is a common attack wherein additional
   query language is inserted within some part of the target URI or
   header fields (e.g., Host, Referer, etc.).  If the received data is
   used directly within a SELECT statement, the query language might be
   interpreted as a database command instead of a simple string value.
   This type of implementation vulnerability is extremely common, in
   spite of being easy to prevent.

   In general, resource implementations ought to avoid use of request
   data in contexts that are processed or interpreted as instructions.
   Parameters ought to be compared to fixed strings and acted upon as a
   result of that comparison, rather than passed through an interface
   that is not prepared for untrusted data.  Received data that isn't
   based on fixed parameters ought to be carefully filtered or encoded
   to avoid being misinterpreted.

   Similar considerations apply to request data when it is stored and
   later processed, such as within log files, monitoring tools, or when
   included within a data format that allows embedded scripts.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
