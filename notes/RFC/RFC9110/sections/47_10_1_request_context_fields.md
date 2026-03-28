---
title: "10.1.  Request Context Fields"
rfc_number: 9110
rfc_section: "10.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 10.1: Request Context Fields — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, request_context_fields]
---

## 10.1.  Request Context Fields

10.  Message Context

## 10.1  Request Context Fields

   The request header fields below provide additional information about
   the request context, including information about the user, user
   agent, and resource behind the request.

### 10.1.1  Expect

   The "Expect" header field in a request indicates a certain set of
   behaviors (expectations) that need to be supported by the server in
   order to properly handle this request.


```abnf
     Expect =      #expectation
     expectation = token [ "=" ( token / quoted-string ) parameters ]
```


   The Expect field value is case-insensitive.

   The only expectation defined by this specification is "100-continue"
   (with no defined parameters).

   A server that receives an Expect field value containing a member
> **MAY**: other than 100-continue MAY respond with a 417 (Expectation Failed)
   status code to indicate that the unexpected expectation cannot be
   met.

   A "100-continue" expectation informs recipients that the client is
   about to send (presumably large) content in this request and wishes
   to receive a 100 (Continue) interim response if the method, target
   URI, and header fields are not sufficient to cause an immediate
   success, redirect, or error response.  This allows the client to wait
   for an indication that it is worthwhile to send the content before
   actually doing so, which can improve efficiency when the data is huge
   or when the client anticipates that an error is likely (e.g., when
   sending a state-changing method, for the first time, without
   previously verified authentication credentials).

   For example, a request that begins with

   PUT /somewhere/fun HTTP/1.1
   Host: origin.example.com
   Content-Type: video/h264
   Content-Length: 1234567890987
   Expect: 100-continue

   allows the origin server to immediately respond with an error
   message, such as 401 (Unauthorized) or 405 (Method Not Allowed),
   before the client starts filling the pipes with an unnecessary data
   transfer.

   Requirements for clients:

> **MUST NOT**: *  A client MUST NOT generate a 100-continue expectation in a request
      that does not include content.

   *  A client that will wait for a 100 (Continue) response before
> **MUST**: sending the request content MUST send an Expect header field
      containing a 100-continue expectation.

   *  A client that sends a 100-continue expectation is not required to
> **MAY**: wait for any specific length of time; such a client MAY proceed to
      send the content even if it has not yet received a response.
      Furthermore, since 100 (Continue) responses cannot be sent through
> **SHOULD NOT**: an HTTP/1.0 intermediary, such a client SHOULD NOT wait for an
      indefinite period before sending the content.

   *  A client that receives a 417 (Expectation Failed) status code in
> **SHOULD**: response to a request containing a 100-continue expectation SHOULD
      repeat that request without a 100-continue expectation, since the
      417 response merely indicates that the response chain does not
      support expectations (e.g., it passes through an HTTP/1.0 server).

   Requirements for servers:

   *  A server that receives a 100-continue expectation in an HTTP/1.0
> **MUST**: request MUST ignore that expectation.

> **MAY**: *  A server MAY omit sending a 100 (Continue) response if it has
      already received some or all of the content for the corresponding
      request, or if the framing indicates that there is no content.

> **MUST**: *  A server that sends a 100 (Continue) response MUST ultimately send
      a final status code, once it receives and processes the request
      content, unless the connection is closed prematurely.

   *  A server that responds with a final status code before reading the
> **SHOULD**: entire request content SHOULD indicate whether it intends to close
      the connection (e.g., see Section 9.6 of [HTTP/1.1]) or continue
      reading the request content.

   Upon receiving an HTTP/1.1 (or later) request that has a method,
   target URI, and complete header section that contains a 100-continue
   expectation and an indication that request content will follow, an
> **MUST**: origin server MUST send either:

   *  an immediate response with a final status code, if that status can
      be determined by examining just the method, target URI, and header
      fields, or

   *  an immediate 100 (Continue) response to encourage the client to
      send the request content.

> **MUST NOT**: The origin server MUST NOT wait for the content before sending the
   100 (Continue) response.

   Upon receiving an HTTP/1.1 (or later) request that has a method,
   target URI, and complete header section that contains a 100-continue
> **MUST**: expectation and indicates a request content will follow, a proxy MUST
   either:

   *  send an immediate response with a final status code, if that
      status can be determined by examining just the method, target URI,
      and header fields, or

   *  forward the request toward the origin server by sending a
      corresponding request-line and header section to the next inbound
      server.

   If the proxy believes (from configuration or past interaction) that
> **MAY**: the next inbound server only supports HTTP/1.0, the proxy MAY
   generate an immediate 100 (Continue) response to encourage the client
   to begin sending the content.

### 10.1.2  From

   The "From" header field contains an Internet email address for a
   human user who controls the requesting user agent.  The address ought
   to be machine-usable, as defined by "mailbox" in Section 3.4 of
   [RFC5322]:


```abnf
     From    = mailbox

     mailbox = <mailbox, see [RFC5322], Section 3.4>
```


   An example is:

   From: spider-admin@example.org

   The From header field is rarely sent by non-robotic user agents.  A
> **SHOULD NOT**: user agent SHOULD NOT send a From header field without explicit
   configuration by the user, since that might conflict with the user's
   privacy interests or their site's security policy.

> **SHOULD**: A robotic user agent SHOULD send a valid From header field so that
   the person responsible for running the robot can be contacted if
   problems occur on servers, such as if the robot is sending excessive,
   unwanted, or invalid requests.

> **SHOULD NOT**: A server SHOULD NOT use the From header field for access control or
   authentication, since its value is expected to be visible to anyone
   receiving or observing the request and is often recorded within
   logfiles and error reports without any expectation of privacy.

### 10.1.3  Referer

   The "Referer" [sic] header field allows the user agent to specify a
   URI reference for the resource from which the target URI was obtained
   (i.e., the "referrer", though the field name is misspelled).  A user
> **MUST NOT**: agent MUST NOT include the fragment and userinfo components of the
   URI reference [URI], if any, when generating the Referer field value.


```abnf
     Referer = absolute-URI / partial-URI
```


   The field value is either an absolute-URI or a partial-URI.  In the
   latter case (Section 4), the referenced URI is relative to the target
   URI ([URI], Section 5).

   The Referer header field allows servers to generate back-links to
   other resources for simple analytics, logging, optimized caching,
   etc.  It also allows obsolete or mistyped links to be found for
   maintenance.  Some servers use the Referer header field as a means of
   denying links from other sites (so-called "deep linking") or
   restricting cross-site request forgery (CSRF), but not all requests
   contain it.

   Example:

   Referer: http://www.example.org/hypertext/Overview.html

   If the target URI was obtained from a source that does not have its
   own URI (e.g., input from the user keyboard, or an entry within the
> **MUST**: user's bookmarks/favorites), the user agent MUST either exclude the
   Referer header field or send it with a value of "about:blank".

   The Referer header field value need not convey the full URI of the
> **MAY**: referring resource; a user agent MAY truncate parts other than the
   referring origin.

   The Referer header field has the potential to reveal information
   about the request context or browsing history of the user, which is a
   privacy concern if the referring resource's identifier reveals
   personal information (such as an account name) or a resource that is
   supposed to be confidential (such as behind a firewall or internal to
   a secured service).  Most general-purpose user agents do not send the
   Referer header field when the referring resource is a local "file" or
> **SHOULD NOT**: "data" URI.  A user agent SHOULD NOT send a Referer header field if
   the referring resource was accessed with a secure protocol and the
   request target has an origin differing from that of the referring
   resource, unless the referring resource explicitly allows Referer to
> **MUST NOT**: be sent.  A user agent MUST NOT send a Referer header field in an
   unsecured HTTP request if the referring resource was accessed with a
   secure protocol.  See Section 17.9 for additional security
   considerations.

   Some intermediaries have been known to indiscriminately remove
   Referer header fields from outgoing requests.  This has the
   unfortunate side effect of interfering with protection against CSRF
   attacks, which can be far more harmful to their users.
   Intermediaries and user agent extensions that wish to limit
   information disclosure in Referer ought to restrict their changes to
   specific edits, such as replacing internal domain names with
   pseudonyms or truncating the query and/or path components.  An
> **SHOULD NOT**: intermediary SHOULD NOT modify or delete the Referer header field
   when the field value shares the same scheme and host as the target
   URI.

10.1.4.  TE

   The "TE" header field describes capabilities of the client with
   regard to transfer codings and trailer sections.

   As described in Section 6.5, a TE field with a "trailers" member sent
   in a request indicates that the client will not discard trailer
   fields.

   TE is also used within HTTP/1.1 to advise servers about which
   transfer codings the client is able to accept in a response.  As of
   publication, only HTTP/1.1 uses transfer codings (see Section 7 of
   [HTTP/1.1]).

   The TE field value is a list of members, with each member (aside from
   "trailers") consisting of a transfer coding name token with an
   optional weight indicating the client's relative preference for that
   transfer coding (Section 12.4.2) and optional parameters for that
   transfer coding.


```abnf
     TE                 = #t-codings
     t-codings          = "trailers" / ( transfer-coding [ weight ] )
     transfer-coding    = token *( OWS ";" OWS transfer-parameter )
     transfer-parameter = token BWS "=" BWS ( token / quoted-string )
```


> **MUST**: A sender of TE MUST also send a "TE" connection option within the
   Connection header field (Section 7.6.1) to inform intermediaries not
   to forward this field.

### 10.1.5  User-Agent

   The "User-Agent" header field contains information about the user
   agent originating the request, which is often used by servers to help
   identify the scope of reported interoperability problems, to work
   around or tailor responses to avoid particular user agent
   limitations, and for analytics regarding browser or operating system
> **SHOULD**: use.  A user agent SHOULD send a User-Agent header field in each
   request unless specifically configured not to do so.


```abnf
     User-Agent = product *( RWS ( product / comment ) )
```


   The User-Agent field value consists of one or more product
   identifiers, each followed by zero or more comments (Section 5.6.5),
   which together identify the user agent software and its significant
   subproducts.  By convention, the product identifiers are listed in
   decreasing order of their significance for identifying the user agent
   software.  Each product identifier consists of a name and optional
   version.


```abnf
     product         = token ["/" product-version]
     product-version = token
```


> **SHOULD**: A sender SHOULD limit generated product identifiers to what is
   necessary to identify the product; a sender MUST NOT generate
   advertising or other nonessential information within the product
> **SHOULD NOT**: identifier.  A sender SHOULD NOT generate information in
   product-version that is not a version identifier (i.e., successive
   versions of the same product name ought to differ only in the
   product-version portion of the product identifier).

   Example:

   User-Agent: CERN-LineMode/2.15 libwww/2.17b3

> **SHOULD NOT**: A user agent SHOULD NOT generate a User-Agent header field containing
   needlessly fine-grained detail and SHOULD limit the addition of
   subproducts by third parties.  Overly long and detailed User-Agent
   field values increase request latency and the risk of a user being
   identified against their wishes ("fingerprinting").

   Likewise, implementations are encouraged not to use the product
   tokens of other implementations in order to declare compatibility
   with them, as this circumvents the purpose of the field.  If a user
   agent masquerades as a different user agent, recipients can assume
   that the user intentionally desires to see responses tailored for
   that identified user agent, even if they might not work as well for
   the actual user agent being used.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
