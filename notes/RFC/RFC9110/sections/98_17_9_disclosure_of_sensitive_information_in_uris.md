---
title: "17.9.  Disclosure of Sensitive Information in URIs"
rfc_number: 9110
rfc_section: "17.9"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.9: Disclosure of Sensitive Information in URIs — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, disclosure_of_sensitive_information_in_uris]
---

## 17.9.  Disclosure of Sensitive Information in URIs

## 17.9  Disclosure of Sensitive Information in URIs

   URIs are intended to be shared, not secured, even when they identify
   secure resources.  URIs are often shown on displays, added to
   templates when a page is printed, and stored in a variety of
   unprotected bookmark lists.  Many servers, proxies, and user agents
   log or display the target URI in places where it might be visible to
   third parties.  It is therefore unwise to include information within
   a URI that is sensitive, personally identifiable, or a risk to
   disclose.

   When an application uses client-side mechanisms to construct a target
   URI out of user-provided information, such as the query fields of a
   form using GET, potentially sensitive data might be provided that
   would not be appropriate for disclosure within a URI.  POST is often
   preferred in such cases because it usually doesn't construct a URI;
   instead, POST of a form transmits the potentially sensitive data in
   the request content.  However, this hinders caching and uses an
   unsafe method for what would otherwise be a safe request.
   Alternative workarounds include transforming the user-provided data
   prior to constructing the URI or filtering the data to only include
   common values that are not sensitive.  Likewise, redirecting the
   result of a query to a different (server-generated) URI can remove
   potentially sensitive data from later links and provide a cacheable
   response for later reuse.

   Since the Referer header field tells a target site about the context
   that resulted in a request, it has the potential to reveal
   information about the user's immediate browsing history and any
   personal information that might be found in the referring resource's
   URI.  Limitations on the Referer header field are described in
   Section 10.1.3 to address some of its security considerations.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
