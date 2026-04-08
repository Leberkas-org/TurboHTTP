---
title: 3.  Request Line
rfc_number: 9112
rfc_section: '3'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 3: Request Line — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
  - request_line
---

## 3.  Request Line

3.  Request Line

   A request-line begins with a method token, followed by a single space
   (SP), the request-target, and another single space (SP), and ends
   with the protocol version.


```abnf
     request-line   = method SP request-target SP HTTP-version
```


   Although the request-line grammar rule requires that each of the
> **MAY**: component elements be separated by a single SP octet, recipients MAY
   instead parse on whitespace-delimited word boundaries and, aside from
   the CRLF terminator, treat any form of whitespace as the SP separator
   while ignoring preceding or trailing whitespace; such whitespace
   includes one or more of the following octets: SP, HTAB, VT (%x0B), FF
   (%x0C), or bare CR.  However, lenient parsing can result in request
   smuggling security vulnerabilities if there are multiple recipients
   of the message and each has its own unique interpretation of
   robustness (see Section 11.2).

   HTTP does not place a predefined limit on the length of a request-
   line, as described in Section 2.3 of [HTTP].  A server that receives
> **SHOULD**: a method longer than any that it implements SHOULD respond with a 501
   (Not Implemented) status code.  A server that receives a request-
> **MUST**: target longer than any URI it wishes to parse MUST respond with a 414
   (URI Too Long) status code (see Section 15.5.15 of [HTTP]).

   Various ad hoc limitations on request-line length are found in
   practice.  It is RECOMMENDED that all HTTP senders and recipients
   support, at a minimum, request-line lengths of 8000 octets.

## 3.1  Method

   The method token indicates the request method to be performed on the
   target resource.  The request method is case-sensitive.


```abnf
     method         = token
```


   The request methods defined by this specification can be found in
   Section 9 of [HTTP], along with information regarding the HTTP method
   registry and considerations for defining new methods.

## 3.2  Request Target

   The request-target identifies the target resource upon which to apply
   the request.  The client derives a request-target from its desired
   target URI.  There are four distinct formats for the request-target,
   depending on both the method being requested and whether the request
   is to a proxy.


```abnf
     request-target = origin-form
                    / absolute-form
                    / authority-form
                    / asterisk-form
```


   No whitespace is allowed in the request-target.  Unfortunately, some
   user agents fail to properly encode or exclude whitespace found in
   hypertext references, resulting in those disallowed characters being
   sent as the request-target in a malformed request-line.

> **SHOULD**: Recipients of an invalid request-line SHOULD respond with either a
   400 (Bad Request) error or a 301 (Moved Permanently) redirect with
> **SHOULD NOT**: the request-target properly encoded.  A recipient SHOULD NOT attempt
   to autocorrect and then process the request without a redirect, since
   the invalid request-line might be deliberately crafted to bypass
   security filters along the request chain.

> **MUST**: A client MUST send a Host header field (Section 7.2 of [HTTP]) in all
   HTTP/1.1 request messages.  If the target URI includes an authority
> **MUST**: component, then a client MUST send a field value for Host that is
   identical to that authority component, excluding any userinfo
   subcomponent and its "@" delimiter (Section 4.2 of [HTTP]).  If the
   authority component is missing or undefined for the target URI, then
> **MUST**: a client MUST send a Host header field with an empty field value.

> **MUST**: A server MUST respond with a 400 (Bad Request) status code to any
   HTTP/1.1 request message that lacks a Host header field and to any
   request message that contains more than one Host header field line or
   a Host header field with an invalid field value.

3.2.1.  origin-form

   The most common form of request-target is the "origin-form".


```abnf
     origin-form    = absolute-path [ "?" query ]
```


   When making a request directly to an origin server, other than a
   CONNECT or server-wide OPTIONS request (as detailed below), a client
> **MUST**: MUST send only the absolute path and query components of the target
   URI as the request-target.  If the target URI's path component is
> **MUST**: empty, the client MUST send "/" as the path within the origin-form of
   request-target.  A Host header field is also sent, as defined in
   Section 7.2 of [HTTP].

   For example, a client wishing to retrieve a representation of the
   resource identified as

     http://www.example.org/where?q=now

   directly from the origin server would open (or reuse) a TCP
   connection to port 80 of the host "www.example.org" and send the
   lines:

   GET /where?q=now HTTP/1.1
   Host: www.example.org

   followed by the remainder of the request message.

3.2.2.  absolute-form

   When making a request to a proxy, other than a CONNECT or server-wide
> **MUST**: OPTIONS request (as detailed below), a client MUST send the target
   URI in "absolute-form" as the request-target.


```abnf
     absolute-form  = absolute-URI
```


   The proxy is requested to either service that request from a valid
   cache, if possible, or make the same request on the client's behalf
   either to the next inbound proxy server or directly to the origin
   server indicated by the request-target.  Requirements on such
   "forwarding" of messages are defined in Section 7.6 of [HTTP].

   An example absolute-form of request-line would be:

   GET http://www.example.org/pub/WWW/TheProject.html HTTP/1.1

> **MUST**: A client MUST send a Host header field in an HTTP/1.1 request even if
   the request-target is in the absolute-form, since this allows the
   Host information to be forwarded through ancient HTTP/1.0 proxies
   that might not have implemented Host.

   When a proxy receives a request with an absolute-form of request-
> **MUST**: target, the proxy MUST ignore the received Host header field (if any)
   and instead replace it with the host information of the request-
> **MUST**: target.  A proxy that forwards such a request MUST generate a new
   Host field value based on the received request-target rather than
   forward the received Host field value.

   When an origin server receives a request with an absolute-form of
> **MUST**: request-target, the origin server MUST ignore the received Host
   header field (if any) and instead use the host information of the
   request-target.  Note that if the request-target does not have an
   authority component, an empty Host header field will be sent in this
   case.

> **MUST**: A server MUST accept the absolute-form in requests even though most
   HTTP/1.1 clients will only send the absolute-form to a proxy.

3.2.3.  authority-form

   The "authority-form" of request-target is only used for CONNECT
   requests (Section 9.3.6 of [HTTP]).  It consists of only the uri-host
   and port number of the tunnel destination, separated by a colon
   (":").


```abnf
     authority-form = uri-host ":" port
```


   When making a CONNECT request to establish a tunnel through one or
> **MUST**: more proxies, a client MUST send only the host and port of the tunnel
   destination as the request-target.  The client obtains the host and
   port from the target URI's authority component, except that it sends
   the scheme's default port if the target URI elides the port.  For
   example, a CONNECT request to "http://www.example.com" looks like the
   following:

   CONNECT www.example.com:80 HTTP/1.1
   Host: www.example.com

3.2.4.  asterisk-form

   The "asterisk-form" of request-target is only used for a server-wide
   OPTIONS request (Section 9.3.7 of [HTTP]).


```abnf
     asterisk-form  = "*"
```


   When a client wishes to request OPTIONS for the server as a whole, as
> **MUST**: opposed to a specific named resource of that server, the client MUST
   send only "*" (%x2A) as the request-target.  For example,

   OPTIONS * HTTP/1.1

   If a proxy receives an OPTIONS request with an absolute-form of
   request-target in which the URI has an empty path and no query
> **MUST**: component, then the last proxy on the request chain MUST send a
   request-target of "*" when it forwards the request to the indicated
   origin server.

   For example, the request

   OPTIONS http://www.example.org:8001 HTTP/1.1

   would be forwarded by the final proxy as

   OPTIONS * HTTP/1.1
   Host: www.example.org:8001

   after connecting to port 8001 of host "www.example.org".

## 3.3  Reconstructing the Target URI

   The target URI is the request-target when the request-target is in
   absolute-form.  In that case, a server will parse the URI into its
   generic components for further evaluation.

   Otherwise, the server reconstructs the target URI from the connection
   context and various parts of the request message in order to identify
   the target resource (Section 7.1 of [HTTP]):

   *  If the server's configuration provides for a fixed URI scheme, or
      a scheme is provided by a trusted outbound gateway, that scheme is
      used for the target URI.  This is common in large-scale
      deployments because a gateway server will receive the client's
      connection context and replace that with their own connection to
      the inbound server.  Otherwise, if the request is received over a
      secured connection, the target URI's scheme is "https"; if not,
      the scheme is "http".

   *  If the request-target is in authority-form, the target URI's
      authority component is the request-target.  Otherwise, the target
      URI's authority component is the field value of the Host header
      field.  If there is no Host header field or if its field value is
      empty or invalid, the target URI's authority component is empty.

   *  If the request-target is in authority-form or asterisk-form, the
      target URI's combined path and query component is empty.
      Otherwise, the target URI's combined path and query component is
      the request-target.

   *  The components of a reconstructed target URI, once determined as
      above, can be recombined into absolute-URI form by concatenating
      the scheme, "://", authority, and combined path and query
      component.

   Example 1: The following message received over a secure connection

   GET /pub/WWW/TheProject.html HTTP/1.1
   Host: www.example.org

   has a target URI of

     https://www.example.org/pub/WWW/TheProject.html

   Example 2: The following message received over an insecure connection

   OPTIONS * HTTP/1.1
   Host: www.example.org:8080

   has a target URI of

     http://www.example.org:8080

   If the target URI's authority component is empty and its URI scheme
   requires a non-empty authority (as is the case for "http" and
   "https"), the server can reject the request or determine whether a
   configured default applies that is consistent with the incoming
   connection's context.  Context might include connection details like
   address and port, what security has been applied, and locally defined
   information specific to that server's configuration.  An empty
   authority is replaced with the configured default before further
   processing of the request.

   Supplying a default name for authority within the context of a
   secured connection is inherently unsafe if there is any chance that
   the user agent's intended authority might differ from the default.  A
   server that can uniquely identify an authority from the request
> **MAY**: context MAY use that identity as a default without this risk.
   Alternatively, it might be better to redirect the request to a safe
   resource that explains how to obtain a new client.

   Note that reconstructing the client's target URI is only half of the
   process for identifying a target resource.  The other half is
   determining whether that target URI identifies a resource for which
   the server is willing and able to send a response, as defined in
   Section 7.4 of [HTTP].


---

## TurboHTTP Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHTTP's `Http11RequestEncoder` generates compliant request-lines with method, request-target (origin-form), and HTTP-version. The Host header is always included in HTTP/1.1 requests. Request-target is derived from the target URI using origin-form (absolute-path + query).

**Key Components:**
- `Http11RequestEncoder` — generates `method SP request-target SP HTTP-version CRLF`
- `HttpRequestEncoder` — prepares request metadata including Host header

**Compliance Details:**
- ✅ Request-line format: `method SP request-target SP HTTP-version`
- ✅ Host header always sent in HTTP/1.1 requests
- ✅ Origin-form used for direct requests (absolute-path + query)
- ✅ Empty path normalized to "/"
- ⚠️ Absolute-form (proxy requests) not currently used (TurboHTTP is not a proxy client)
- ⚠️ Authority-form (CONNECT) not supported
- ⚠️ Asterisk-form (OPTIONS *) not supported

**Gaps:**
- No proxy-style absolute-form request-target
- No CONNECT method support
- No OPTIONS * (server-wide) support

**Test References:** `TurboHTTP.Tests.RFC9112`

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
