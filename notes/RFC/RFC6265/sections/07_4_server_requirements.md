---
title: "4.  Server Requirements"
rfc_number: 6265
rfc_section: "4"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 4: Server Requirements — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, server_requirements]
---

# 4.  Server Requirements


   This section describes the syntax and semantics of a well-behaved
   profile of the Cookie and Set-Cookie headers.

## 4.1.  Set-Cookie

   The Set-Cookie HTTP response header is used to send cookies from the
   server to the user agent.

### 4.1.1.  Syntax

   Informally, the Set-Cookie response header contains the header name
   "Set-Cookie" followed by a ":" and a cookie.  Each cookie begins with
   a name-value-pair, followed by zero or more attribute-value pairs.
> **SHOULD NOT**: Servers SHOULD NOT send Set-Cookie headers that fail to conform to
   the following grammar:












 set-cookie-header = "Set-Cookie:" SP set-cookie-string
 set-cookie-string = cookie-pair *( ";" SP cookie-av )
 cookie-pair       = cookie-name "=" cookie-value
 cookie-name       = token
 cookie-value      = *cookie-octet / ( DQUOTE *cookie-octet DQUOTE )
 cookie-octet      = %x21 / %x23-2B / %x2D-3A / %x3C-5B / %x5D-7E
                       ; US-ASCII characters excluding CTLs,
                       ; whitespace DQUOTE, comma, semicolon,
                       ; and backslash
 token             = <token, defined in [RFC2616], Section 2.2>

 cookie-av         = expires-av / max-age-av / domain-av /
                     path-av / secure-av / httponly-av /
                     extension-av
 expires-av        = "Expires=" sane-cookie-date
 sane-cookie-date  = <rfc1123-date, defined in [RFC2616], Section 3.3.1>
 max-age-av        = "Max-Age=" non-zero-digit *DIGIT
                       ; In practice, both expires-av and max-age-av
                       ; are limited to dates representable by the
                       ; user agent.
 non-zero-digit    = %x31-39
                       ; digits 1 through 9
 domain-av         = "Domain=" domain-value
 domain-value      = <subdomain>
                       ; defined in [RFC1034], Section 3.5, as
                       ; enhanced by [RFC1123], Section 2.1
 path-av           = "Path=" path-value
 path-value        = <any CHAR except CTLs or ";">
 secure-av         = "Secure"
 httponly-av       = "HttpOnly"
 extension-av      = <any CHAR except CTLs or ";">

   Note that some of the grammatical terms above reference documents
   that use different grammatical notations than this document (which
   uses ABNF from [RFC5234]).

   The semantics of the cookie-value are not defined by this document.

   To maximize compatibility with user agents, servers that wish to
> **SHOULD**: store arbitrary data in a cookie-value SHOULD encode that data, for
   example, using Base64 [RFC4648].

   The portions of the set-cookie-string produced by the cookie-av term
   are known as attributes.  To maximize compatibility with user agents,
> **SHOULD NOT**: servers SHOULD NOT produce two attributes with the same name in the
   same set-cookie-string.  (See Section 5.3 for how user agents handle
   this case.)




> **SHOULD NOT**: Servers SHOULD NOT include more than one Set-Cookie header field in
   the same response with the same cookie-name.  (See Section 5.2 for
   how user agents handle this case.)

   If a server sends multiple responses containing Set-Cookie headers
   concurrently to the user agent (e.g., when communicating with the
   user agent over multiple sockets), these responses create a "race
   condition" that can lead to unpredictable behavior.

   NOTE: Some existing user agents differ in their interpretation of
> **SHOULD**: two-digit years.  To avoid compatibility issues, servers SHOULD use
   the rfc1123-date format, which requires a four-digit year.

   NOTE: Some user agents store and process dates in cookies as 32-bit
   UNIX time_t values.  Implementation bugs in the libraries supporting
   time_t processing on some systems might cause such user agents to
   process dates after the year 2038 incorrectly.

### 4.1.2.  Semantics (Non-Normative)

   This section describes simplified semantics of the Set-Cookie header.
   These semantics are detailed enough to be useful for understanding
   the most common uses of cookies by servers.  The full semantics are
   described in Section 5.

   When the user agent receives a Set-Cookie header, the user agent
   stores the cookie together with its attributes.  Subsequently, when
   the user agent makes an HTTP request, the user agent includes the
   applicable, non-expired cookies in the Cookie header.

   If the user agent receives a new cookie with the same cookie-name,
   domain-value, and path-value as a cookie that it has already stored,
   the existing cookie is evicted and replaced with the new cookie.
   Notice that servers can delete cookies by sending the user agent a
   new cookie with an Expires attribute with a value in the past.

   Unless the cookie's attributes indicate otherwise, the cookie is
   returned only to the origin server (and not, for example, to any
   subdomains), and it expires at the end of the current session (as
   defined by the user agent).  User agents ignore unrecognized cookie
   attributes (but not the entire cookie).










### 4.1.2.1.  The Expires Attribute

   The Expires attribute indicates the maximum lifetime of the cookie,
   represented as the date and time at which the cookie expires.  The
   user agent is not required to retain the cookie until the specified
   date has passed.  In fact, user agents often evict cookies due to
   memory pressure or privacy concerns.

### 4.1.2.2.  The Max-Age Attribute

   The Max-Age attribute indicates the maximum lifetime of the cookie,
   represented as the number of seconds until the cookie expires.  The
   user agent is not required to retain the cookie for the specified
   duration.  In fact, user agents often evict cookies due to memory
   pressure or privacy concerns.

      NOTE: Some existing user agents do not support the Max-Age
      attribute.  User agents that do not support the Max-Age attribute
      ignore the attribute.

   If a cookie has both the Max-Age and the Expires attribute, the Max-
   Age attribute has precedence and controls the expiration date of the
   cookie.  If a cookie has neither the Max-Age nor the Expires
   attribute, the user agent will retain the cookie until "the current
   session is over" (as defined by the user agent).

### 4.1.2.3.  The Domain Attribute

   The Domain attribute specifies those hosts to which the cookie will
   be sent.  For example, if the value of the Domain attribute is
   "example.com", the user agent will include the cookie in the Cookie
   header when making HTTP requests to example.com, www.example.com, and
   www.corp.example.com.  (Note that a leading %x2E ("."), if present,
   is ignored even though that character is not permitted, but a
   trailing %x2E ("."), if present, will cause the user agent to ignore
   the attribute.)  If the server omits the Domain attribute, the user
   agent will return the cookie only to the origin server.

      WARNING: Some existing user agents treat an absent Domain
      attribute as if the Domain attribute were present and contained
      the current host name.  For example, if example.com returns a Set-
      Cookie header without a Domain attribute, these user agents will
      erroneously send the cookie to www.example.com as well.








   The user agent will reject cookies unless the Domain attribute
   specifies a scope for the cookie that would include the origin
   server.  For example, the user agent will accept a cookie with a
   Domain attribute of "example.com" or of "foo.example.com" from
   foo.example.com, but the user agent will not accept a cookie with a
   Domain attribute of "bar.example.com" or of "baz.foo.example.com".

   NOTE: For security reasons, many user agents are configured to reject
   Domain attributes that correspond to "public suffixes".  For example,
   some user agents will reject Domain attributes of "com" or "co.uk".
   (See Section 5.3 for more information.)

### 4.1.2.4.  The Path Attribute

   The scope of each cookie is limited to a set of paths, controlled by
   the Path attribute.  If the server omits the Path attribute, the user
   agent will use the "directory" of the request-uri's path component as
   the default value.  (See Section 5.1.4 for more details.)

   The user agent will include the cookie in an HTTP request only if the
   path portion of the request-uri matches (or is a subdirectory of) the
   cookie's Path attribute, where the %x2F ("/") character is
   interpreted as a directory separator.

   Although seemingly useful for isolating cookies between different
   paths within a given host, the Path attribute cannot be relied upon
   for security (see Section 8).

### 4.1.2.5.  The Secure Attribute

   The Secure attribute limits the scope of the cookie to "secure"
   channels (where "secure" is defined by the user agent).  When a
   cookie has the Secure attribute, the user agent will include the
   cookie in an HTTP request only if the request is transmitted over a
   secure channel (typically HTTP over Transport Layer Security (TLS)
   [RFC2818]).

   Although seemingly useful for protecting cookies from active network
   attackers, the Secure attribute protects only the cookie's
   confidentiality.  An active network attacker can overwrite Secure
   cookies from an insecure channel, disrupting their integrity (see
   Section 8.6 for more details).









### 4.1.2.6.  The HttpOnly Attribute

   The HttpOnly attribute limits the scope of the cookie to HTTP
   requests.  In particular, the attribute instructs the user agent to
   omit the cookie when providing access to cookies via "non-HTTP" APIs
   (such as a web browser API that exposes cookies to scripts).

   Note that the HttpOnly attribute is independent of the Secure
   attribute: a cookie can have both the HttpOnly and the Secure
   attribute.

## 4.2.  Cookie

### 4.2.1.  Syntax

   The user agent sends stored cookies to the origin server in the
   Cookie header.  If the server conforms to the requirements in
   Section 4.1 (and the user agent conforms to the requirements in
   Section 5), the user agent will send a Cookie header that conforms to
   the following grammar:


```abnf
   cookie-header = "Cookie:" OWS cookie-string OWS
   cookie-string = cookie-pair *( ";" SP cookie-pair )
```


### 4.2.2.  Semantics

   Each cookie-pair represents a cookie stored by the user agent.  The
   cookie-pair contains the cookie-name and cookie-value the user agent
   received in the Set-Cookie header.

   Notice that the cookie attributes are not returned.  In particular,
   the server cannot determine from the Cookie header alone when a
   cookie will expire, for which hosts the cookie is valid, for which
   paths the cookie is valid, or whether the cookie was set with the
   Secure or HttpOnly attributes.

   The semantics of individual cookies in the Cookie header are not
   defined by this document.  Servers are expected to imbue these
   cookies with application-specific semantics.

   Although cookies are serialized linearly in the Cookie header,
> **SHOULD NOT**: servers SHOULD NOT rely upon the serialization order.  In particular,
   if the Cookie header contains two cookies with the same name (e.g.,
   that were set with different Path or Domain attributes), servers
> **SHOULD NOT**: SHOULD NOT rely upon the order in which these cookies appear in the
   header.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
