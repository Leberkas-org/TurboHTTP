---
title: "10.2.  Response Context Fields"
rfc_number: 9110
rfc_section: "10.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 10.2: Response Context Fields — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, response_context_fields]
---

## 10.2.  Response Context Fields

## 10.2  Response Context Fields

   The response header fields below provide additional information about
   the response, beyond what is implied by the status code, including
   information about the server, about the target resource, or about
   related resources.

### 10.2.1  Allow

   The "Allow" header field lists the set of methods advertised as
   supported by the target resource.  The purpose of this field is
   strictly to inform the recipient of valid request methods associated
   with the resource.


```abnf
     Allow = #method
```


   Example of use:

   Allow: GET, HEAD, PUT

   The actual set of allowed methods is defined by the origin server at
> **MUST**: the time of each request.  An origin server MUST generate an Allow
   header field in a 405 (Method Not Allowed) response and MAY do so in
   any other response.  An empty Allow field value indicates that the
   resource allows no methods, which might occur in a 405 response if
   the resource has been temporarily disabled by configuration.

> **MUST NOT**: A proxy MUST NOT modify the Allow header field -- it does not need to
   understand all of the indicated methods in order to handle them
   according to the generic message handling rules.

### 10.2.2  Location

   The "Location" header field is used in some responses to refer to a
   specific resource in relation to the response.  The type of
   relationship is defined by the combination of request method and
   status code semantics.


```abnf
     Location = URI-reference
```


   The field value consists of a single URI-reference.  When it has the
   form of a relative reference ([URI], Section 4.2), the final value is
   computed by resolving it against the target URI ([URI], Section 5).

   For 201 (Created) responses, the Location value refers to the primary
   resource created by the request.  For 3xx (Redirection) responses,
   the Location value refers to the preferred target resource for
   automatically redirecting the request.

   If the Location value provided in a 3xx (Redirection) response does
> **MUST**: not have a fragment component, a user agent MUST process the
   redirection as if the value inherits the fragment component of the
   URI reference used to generate the target URI (i.e., the redirection
   inherits the original reference's fragment, if any).

   For example, a GET request generated for the URI reference
   "http://www.example.org/~tim" might result in a 303 (See Other)
   response containing the header field:

   Location: /People.html#tim

   which suggests that the user agent redirect to
   "http://www.example.org/People.html#tim"

   Likewise, a GET request generated for the URI reference
   "http://www.example.org/index.html#larry" might result in a 301
   (Moved Permanently) response containing the header field:

   Location: http://www.example.net/index.html

   which suggests that the user agent redirect to
   "http://www.example.net/index.html#larry", preserving the original
   fragment identifier.

   There are circumstances in which a fragment identifier in a Location
   value would not be appropriate.  For example, the Location header
   field in a 201 (Created) response is supposed to provide a URI that
   is specific to the created resource.

      |  *Note:* Some recipients attempt to recover from Location header
      |  fields that are not valid URI references.  This specification
      |  does not mandate or define such processing, but does allow it
      |  for the sake of robustness.  A Location field value cannot
      |  allow a list of members because the comma list separator is a
      |  valid data character within a URI-reference.  If an invalid
      |  message is sent with multiple Location field lines, a recipient
      |  along the path might combine those field lines into one value.
      |  Recovery of a valid Location field value from that situation is
      |  difficult and not interoperable across implementations.

      |  *Note:* The Content-Location header field (Section 8.7) differs
      |  from Location in that the Content-Location refers to the most
      |  specific resource corresponding to the enclosed representation.
      |  It is therefore possible for a response to contain both the
      |  Location and Content-Location header fields.

### 10.2.3  Retry-After

   Servers send the "Retry-After" header field to indicate how long the
   user agent ought to wait before making a follow-up request.  When
   sent with a 503 (Service Unavailable) response, Retry-After indicates
   how long the service is expected to be unavailable to the client.
   When sent with any 3xx (Redirection) response, Retry-After indicates
   the minimum time that the user agent is asked to wait before issuing
   the redirected request.

   The Retry-After field value can be either an HTTP-date or a number of
   seconds to delay after receiving the response.


```abnf
     Retry-After = HTTP-date / delay-seconds
```


   A delay-seconds value is a non-negative decimal integer, representing
   time in seconds.


```abnf
     delay-seconds  = 1*DIGIT
```


   Two examples of its use are

   Retry-After: Fri, 31 Dec 1999 23:59:59 GMT
   Retry-After: 120

   In the latter example, the delay is 2 minutes.

### 10.2.4  Server

   The "Server" header field contains information about the software
   used by the origin server to handle the request, which is often used
   by clients to help identify the scope of reported interoperability
   problems, to work around or tailor requests to avoid particular
   server limitations, and for analytics regarding server or operating
> **MAY**: system use.  An origin server MAY generate a Server header field in
   its responses.


```abnf
     Server = product *( RWS ( product / comment ) )
```


   The Server header field value consists of one or more product
   identifiers, each followed by zero or more comments (Section 5.6.5),
   which together identify the origin server software and its
   significant subproducts.  By convention, the product identifiers are
   listed in decreasing order of their significance for identifying the
   origin server software.  Each product identifier consists of a name
   and optional version, as defined in Section 10.1.5.

   Example:

   Server: CERN/3.0 libwww/2.17

> **SHOULD NOT**: An origin server SHOULD NOT generate a Server header field containing
   needlessly fine-grained detail and SHOULD limit the addition of
   subproducts by third parties.  Overly long and detailed Server field
   values increase response latency and potentially reveal internal
   implementation details that might make it (slightly) easier for
   attackers to find and exploit known security holes.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
