---
title: "16.1.  Method Extensibility"
rfc_number: 9110
rfc_section: "16.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 16.1: Method Extensibility — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, method_extensibility]
---

## 16.1.  Method Extensibility

16.  Extending HTTP

   HTTP defines a number of generic extension points that can be used to
   introduce capabilities to the protocol without introducing a new
   version, including methods, status codes, field names, and further
   extensibility points within defined fields, such as authentication
   schemes and cache directives (see Cache-Control extensions in
   Section 5.2.3 of [CACHING]).  Because the semantics of HTTP are not
   versioned, these extension points are persistent; the version of the
   protocol in use does not affect their semantics.

   Version-independent extensions are discouraged from depending on or
   interacting with the specific version of the protocol in use.  When
   this is unavoidable, careful consideration needs to be given to how
   the extension can interoperate across versions.

   Additionally, specific versions of HTTP might have their own
   extensibility points, such as transfer codings in HTTP/1.1
   (Section 6.1 of [HTTP/1.1]) and HTTP/2 SETTINGS or frame types
   ([HTTP/2]).  These extension points are specific to the version of
   the protocol they occur within.

   Version-specific extensions cannot override or modify the semantics
   of a version-independent mechanism or extension point (like a method
   or header field) without explicitly being allowed by that protocol
   element.  For example, the CONNECT method (Section 9.3.6) allows
   this.

   These guidelines assure that the protocol operates correctly and
   predictably, even when parts of the path implement different versions
   of HTTP.

## 16.1  Method Extensibility

### 16.1.1  Method Registry

   The "Hypertext Transfer Protocol (HTTP) Method Registry", maintained
   by IANA at <https://www.iana.org/assignments/http-methods>, registers
   method names.

> **MUST**: HTTP method registrations MUST include the following fields:

   *  Method Name (see Section 9)

   *  Safe ("yes" or "no", see Section 9.2.1)

   *  Idempotent ("yes" or "no", see Section 9.2.2)

   *  Pointer to specification text

   Values to be added to this namespace require IETF Review (see
   [RFC8126], Section 4.8).

### 16.1.2  Considerations for New Methods

   Standardized methods are generic; that is, they are potentially
   applicable to any resource, not just one particular media type, kind
   of resource, or application.  As such, it is preferred that new
   methods be registered in a document that isn't specific to a single
   application or data format, since orthogonal technologies deserve
   orthogonal specification.

   Since message parsing (Section 6) needs to be independent of method
   semantics (aside from responses to HEAD), definitions of new methods
   cannot change the parsing algorithm or prohibit the presence of
   content on either the request or the response message.  Definitions
   of new methods can specify that only a zero-length content is allowed
   by requiring a Content-Length header field with a value of "0".

   Likewise, new methods cannot use the special host:port and asterisk
   forms of request target that are allowed for CONNECT and OPTIONS,
   respectively (Section 7.1).  A full URI in absolute form is needed
   for the target URI, which means either the request target needs to be
   sent in absolute form or the target URI will be reconstructed from
   the request context in the same way it is for other methods.

   A new method definition needs to indicate whether it is safe
   (Section 9.2.1), idempotent (Section 9.2.2), cacheable
   (Section 9.2.3), what semantics are to be associated with the request
   content (if any), and what refinements the method makes to header
   field or status code semantics.  If the new method is cacheable, its
   definition ought to describe how, and under what conditions, a cache
   can store a response and use it to satisfy a subsequent request.  The
   new method ought to describe whether it can be made conditional
   (Section 13.1) and, if so, how a server responds when the condition
   is false.  Likewise, if the new method might have some use for
   partial response semantics (Section 14.2), it ought to document this,
   too.

      |  *Note:* Avoid defining a method name that starts with "M-",
      |  since that prefix might be misinterpreted as having the
      |  semantics assigned to it by [RFC2774].

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
