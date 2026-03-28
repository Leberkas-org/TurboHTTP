---
title: "17.10.  Application Handling of Field Names"
rfc_number: 9110
rfc_section: "17.10"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.10: Application Handling of Field Names — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, application_handling_of_field_names]
---

## 17.10.  Application Handling of Field Names

## 17.10  Application Handling of Field Names

   Servers often use non-HTTP gateway interfaces and frameworks to
   process a received request and produce content for the response.  For
   historical reasons, such interfaces often pass received field names
   as external variable names, using a name mapping suitable for
   environment variables.

   For example, the Common Gateway Interface (CGI) mapping of protocol-
   specific meta-variables, defined by Section 4.1.18 of [RFC3875], is
   applied to received header fields that do not correspond to one of
   CGI's standard variables; the mapping consists of prepending "HTTP_"
   to each name and changing all instances of hyphen ("-") to underscore
   ("_").  This same mapping has been inherited by many other
   application frameworks in order to simplify moving applications from
   one platform to the next.

   In CGI, a received Content-Length field would be passed as the meta-
   variable "CONTENT_LENGTH" with a string value matching the received
   field's value.  In contrast, a received "Content_Length" header field
   would be passed as the protocol-specific meta-variable
   "HTTP_CONTENT_LENGTH", which might lead to some confusion if an
   application mistakenly reads the protocol-specific meta-variable
   instead of the default one.  (This historical practice is why
   Section 16.3.2.1 discourages the creation of new field names that
   contain an underscore.)

   Unfortunately, mapping field names to different interface names can
   lead to security vulnerabilities if the mapping is incomplete or
   ambiguous.  For example, if an attacker were to send a field named
   "Transfer_Encoding", a naive interface might map that to the same
   variable name as the "Transfer-Encoding" field, resulting in a
   potential request smuggling vulnerability (Section 11.2 of
   [HTTP/1.1]).

   To mitigate the associated risks, implementations that perform such
   mappings are advised to make the mapping unambiguous and complete for
   the full range of potential octets received as a name (including
   those that are discouraged or forbidden by the HTTP grammar).  For
   example, a field with an unusual name character might result in the
   request being blocked, the specific field being removed, or the name
   being passed with a different prefix to distinguish it from other
   fields.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
