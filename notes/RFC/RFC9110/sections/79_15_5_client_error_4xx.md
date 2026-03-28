---
title: "15.5.  Client Error 4xx"
rfc_number: 9110
rfc_section: "15.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 15.5: Client Error 4xx — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, client_error_4xx]
---

## 15.5.  Client Error 4xx

## 15.5  Client Error 4xx

   The 4xx (Client Error) class of status code indicates that the client
   seems to have erred.  Except when responding to a HEAD request, the
> **SHOULD**: server SHOULD send a representation containing an explanation of the
   error situation, and whether it is a temporary or permanent
   condition.  These status codes are applicable to any request method.
> **SHOULD**: User agents SHOULD display any included representation to the user.

15.5.1.  400 Bad Request

   The 400 (Bad Request) status code indicates that the server cannot or
   will not process the request due to something that is perceived to be
   a client error (e.g., malformed request syntax, invalid request
   message framing, or deceptive request routing).

15.5.2.  401 Unauthorized

   The 401 (Unauthorized) status code indicates that the request has not
   been applied because it lacks valid authentication credentials for
> **MUST**: the target resource.  The server generating a 401 response MUST send
   a WWW-Authenticate header field (Section 11.6.1) containing at least
   one challenge applicable to the target resource.

   If the request included authentication credentials, then the 401
   response indicates that authorization has been refused for those
> **MAY**: credentials.  The user agent MAY repeat the request with a new or
   replaced Authorization header field (Section 11.6.2).  If the 401
   response contains the same challenge as the prior response, and the
   user agent has already attempted authentication at least once, then
> **SHOULD**: the user agent SHOULD present the enclosed representation to the
   user, since it usually contains relevant diagnostic information.

15.5.3.  402 Payment Required

   The 402 (Payment Required) status code is reserved for future use.

15.5.4.  403 Forbidden

   The 403 (Forbidden) status code indicates that the server understood
   the request but refuses to fulfill it.  A server that wishes to make
   public why the request has been forbidden can describe that reason in
   the response content (if any).

   If authentication credentials were provided in the request, the
   server considers them insufficient to grant access.  The client
> **SHOULD NOT**: SHOULD NOT automatically repeat the request with the same
   credentials.  The client MAY repeat the request with new or different
   credentials.  However, a request might be forbidden for reasons
   unrelated to the credentials.

   An origin server that wishes to "hide" the current existence of a
> **MAY**: forbidden target resource MAY instead respond with a status code of
   404 (Not Found).

15.5.5.  404 Not Found

   The 404 (Not Found) status code indicates that the origin server did
   not find a current representation for the target resource or is not
   willing to disclose that one exists.  A 404 status code does not
   indicate whether this lack of representation is temporary or
   permanent; the 410 (Gone) status code is preferred over 404 if the
   origin server knows, presumably through some configurable means, that
   the condition is likely to be permanent.

   A 404 response is heuristically cacheable; i.e., unless otherwise
   indicated by the method definition or explicit cache controls (see
   Section 4.2.2 of [CACHING]).

15.5.6.  405 Method Not Allowed

   The 405 (Method Not Allowed) status code indicates that the method
   received in the request-line is known by the origin server but not
> **MUST**: supported by the target resource.  The origin server MUST generate an
   Allow header field in a 405 response containing a list of the target
   resource's currently supported methods.

   A 405 response is heuristically cacheable; i.e., unless otherwise
   indicated by the method definition or explicit cache controls (see
   Section 4.2.2 of [CACHING]).

15.5.7.  406 Not Acceptable

   The 406 (Not Acceptable) status code indicates that the target
   resource does not have a current representation that would be
   acceptable to the user agent, according to the proactive negotiation
   header fields received in the request (Section 12.1), and the server
   is unwilling to supply a default representation.

> **SHOULD**: The server SHOULD generate content containing a list of available
   representation characteristics and corresponding resource identifiers
   from which the user or user agent can choose the one most
> **MAY**: appropriate.  A user agent MAY automatically select the most
   appropriate choice from that list.  However, this specification does
   not define any standard for such automatic selection, as described in
   Section 15.4.1.

15.5.8.  407 Proxy Authentication Required

   The 407 (Proxy Authentication Required) status code is similar to 401
   (Unauthorized), but it indicates that the client needs to
   authenticate itself in order to use a proxy for this request.  The
> **MUST**: proxy MUST send a Proxy-Authenticate header field (Section 11.7.1)
   containing a challenge applicable to that proxy for the request.  The
> **MAY**: client MAY repeat the request with a new or replaced
   Proxy-Authorization header field (Section 11.7.2).

15.5.9.  408 Request Timeout

   The 408 (Request Timeout) status code indicates that the server did
   not receive a complete request message within the time that it was
   prepared to wait.

> **MAY**: If the client has an outstanding request in transit, it MAY repeat
   that request.  If the current connection is not usable (e.g., as it
   would be in HTTP/1.1 because request delimitation is lost), a new
   connection will be used.

15.5.10.  409 Conflict

   The 409 (Conflict) status code indicates that the request could not
   be completed due to a conflict with the current state of the target
   resource.  This code is used in situations where the user might be
   able to resolve the conflict and resubmit the request.  The server
> **SHOULD**: SHOULD generate content that includes enough information for a user
   to recognize the source of the conflict.

   Conflicts are most likely to occur in response to a PUT request.  For
   example, if versioning were being used and the representation being
   PUT included changes to a resource that conflict with those made by
   an earlier (third-party) request, the origin server might use a 409
   response to indicate that it can't complete the request.  In this
   case, the response representation would likely contain information
   useful for merging the differences based on the revision history.

15.5.11.  410 Gone

   The 410 (Gone) status code indicates that access to the target
   resource is no longer available at the origin server and that this
   condition is likely to be permanent.  If the origin server does not
   know, or has no facility to determine, whether or not the condition
   is permanent, the status code 404 (Not Found) ought to be used
   instead.

   The 410 response is primarily intended to assist the task of web
   maintenance by notifying the recipient that the resource is
   intentionally unavailable and that the server owners desire that
   remote links to that resource be removed.  Such an event is common
   for limited-time, promotional services and for resources belonging to
   individuals no longer associated with the origin server's site.  It
   is not necessary to mark all permanently unavailable resources as
   "gone" or to keep the mark for any length of time -- that is left to
   the discretion of the server owner.

   A 410 response is heuristically cacheable; i.e., unless otherwise
   indicated by the method definition or explicit cache controls (see
   Section 4.2.2 of [CACHING]).

15.5.12.  411 Length Required

   The 411 (Length Required) status code indicates that the server
   refuses to accept the request without a defined Content-Length
> **MAY**: (Section 8.6).  The client MAY repeat the request if it adds a valid
   Content-Length header field containing the length of the request
   content.

15.5.13.  412 Precondition Failed

   The 412 (Precondition Failed) status code indicates that one or more
   conditions given in the request header fields evaluated to false when
   tested on the server (Section 13).  This response status code allows
   the client to place preconditions on the current resource state (its
   current representations and metadata) and, thus, prevent the request
   method from being applied if the target resource is in an unexpected
   state.

15.5.14.  413 Content Too Large

   The 413 (Content Too Large) status code indicates that the server is
   refusing to process a request because the request content is larger
> **MAY**: than the server is willing or able to process.  The server MAY
   terminate the request, if the protocol version in use allows it;
> **MAY**: otherwise, the server MAY close the connection.

> **SHOULD**: If the condition is temporary, the server SHOULD generate a
   Retry-After header field to indicate that it is temporary and after
> **MAY**: what time the client MAY try again.

15.5.15.  414 URI Too Long

   The 414 (URI Too Long) status code indicates that the server is
   refusing to service the request because the target URI is longer than
   the server is willing to interpret.  This rare condition is only
   likely to occur when a client has improperly converted a POST request
   to a GET request with long query information, when the client has
   descended into an infinite loop of redirection (e.g., a redirected
   URI prefix that points to a suffix of itself) or when the server is
   under attack by a client attempting to exploit potential security
   holes.

   A 414 response is heuristically cacheable; i.e., unless otherwise
   indicated by the method definition or explicit cache controls (see
   Section 4.2.2 of [CACHING]).

15.5.16.  415 Unsupported Media Type

   The 415 (Unsupported Media Type) status code indicates that the
   origin server is refusing to service the request because the content
   is in a format not supported by this method on the target resource.

   The format problem might be due to the request's indicated
   Content-Type or Content-Encoding, or as a result of inspecting the
   data directly.

   If the problem was caused by an unsupported content coding, the
   Accept-Encoding response header field (Section 12.5.3) ought to be
   used to indicate which (if any) content codings would have been
   accepted in the request.

   On the other hand, if the cause was an unsupported media type, the
   Accept response header field (Section 12.5.1) can be used to indicate
   which media types would have been accepted in the request.

15.5.17.  416 Range Not Satisfiable

   The 416 (Range Not Satisfiable) status code indicates that the set of
   ranges in the request's Range header field (Section 14.2) has been
   rejected either because none of the requested ranges are satisfiable
   or because the client has requested an excessive number of small or
   overlapping ranges (a potential denial of service attack).

   Each range unit defines what is required for its own range sets to be
   satisfiable.  For example, Section 14.1.2 defines what makes a bytes
   range set satisfiable.

> **SHOULD**: A server that generates a 416 response to a byte-range request SHOULD
   generate a Content-Range header field specifying the current length
   of the selected representation (Section 14.4).

   For example:

   HTTP/1.1 416 Range Not Satisfiable
   Date: Fri, 20 Jan 2012 15:41:54 GMT
   Content-Range: bytes */47022

      |  *Note:* Because servers are free to ignore Range, many
      |  implementations will respond with the entire selected
      |  representation in a 200 (OK) response.  That is partly because
      |  most clients are prepared to receive a 200 (OK) to complete the
      |  task (albeit less efficiently) and partly because clients might
      |  not stop making an invalid range request until they have
      |  received a complete representation.  Thus, clients cannot
      |  depend on receiving a 416 (Range Not Satisfiable) response even
      |  when it is most appropriate.

15.5.18.  417 Expectation Failed

   The 417 (Expectation Failed) status code indicates that the
   expectation given in the request's Expect header field
   (Section 10.1.1) could not be met by at least one of the inbound
   servers.

15.5.19.  418 (Unused)

   [RFC2324] was an April 1 RFC that lampooned the various ways HTTP was
   abused; one such abuse was the definition of an application-specific
   418 status code, which has been deployed as a joke often enough for
   the code to be unusable for any future use.

   Therefore, the 418 status code is reserved in the IANA HTTP Status
   Code Registry.  This indicates that the status code cannot be
   assigned to other applications currently.  If future circumstances
   require its use (e.g., exhaustion of 4NN status codes), it can be re-
   assigned to another use.

15.5.20.  421 Misdirected Request

   The 421 (Misdirected Request) status code indicates that the request
   was directed at a server that is unable or unwilling to produce an
   authoritative response for the target URI.  An origin server (or
   gateway acting on behalf of the origin server) sends 421 to reject a
   target URI that does not match an origin for which the server has
   been configured (Section 4.3.1) or does not match the connection
   context over which the request was received (Section 7.4).

> **MAY**: A client that receives a 421 (Misdirected Request) response MAY retry
   the request, whether or not the request method is idempotent, over a
   different connection, such as a fresh connection specific to the
   target resource's origin, or via an alternative service [ALTSVC].

> **MUST NOT**: A proxy MUST NOT generate a 421 response.

15.5.21.  422 Unprocessable Content

   The 422 (Unprocessable Content) status code indicates that the server
   understands the content type of the request content (hence a 415
   (Unsupported Media Type) status code is inappropriate), and the
   syntax of the request content is correct, but it was unable to
   process the contained instructions.  For example, this status code
   can be sent if an XML request content contains well-formed (i.e.,
   syntactically correct), but semantically erroneous XML instructions.

15.5.22.  426 Upgrade Required

   The 426 (Upgrade Required) status code indicates that the server
   refuses to perform the request using the current protocol but might
   be willing to do so after the client upgrades to a different
> **MUST**: protocol.  The server MUST send an Upgrade header field in a 426
   response to indicate the required protocol(s) (Section 7.8).

   Example:

   HTTP/1.1 426 Upgrade Required
   Upgrade: HTTP/3.0
   Connection: Upgrade
   Content-Length: 53
   Content-Type: text/plain

   This service requires use of the HTTP/3.0 protocol.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
