---
title: "8.  Security Considerations"
rfc_number: 6265
rfc_section: "8"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 8: Security Considerations — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, security_considerations]
---

# 8.  Security Considerations


## 8.1.  Overview

   Cookies have a number of security pitfalls.  This section overviews a
   few of the more salient issues.

   In particular, cookies encourage developers to rely on ambient
   authority for authentication, often becoming vulnerable to attacks
   such as cross-site request forgery [CSRF].  Also, when storing
   session identifiers in cookies, developers often create session
   fixation vulnerabilities.

   Transport-layer encryption, such as that employed in HTTPS, is
   insufficient to prevent a network attacker from obtaining or altering
   a victim's cookies because the cookie protocol itself has various
   vulnerabilities (see "Weak Confidentiality" and "Weak Integrity",




   below).  In addition, by default, cookies do not provide
   confidentiality or integrity from network attackers, even when used
   in conjunction with HTTPS.

## 8.2.  Ambient Authority

   A server that uses cookies to authenticate users can suffer security
   vulnerabilities because some user agents let remote parties issue
   HTTP requests from the user agent (e.g., via HTTP redirects or HTML
   forms).  When issuing those requests, user agents attach cookies even
   if the remote party does not know the contents of the cookies,
   potentially letting the remote party exercise authority at an unwary
   server.

   Although this security concern goes by a number of names (e.g.,
   cross-site request forgery, confused deputy), the issue stems from
   cookies being a form of ambient authority.  Cookies encourage server
   operators to separate designation (in the form of URLs) from
   authorization (in the form of cookies).  Consequently, the user agent
   might supply the authorization for a resource designated by the
   attacker, possibly causing the server or its clients to undertake
   actions designated by the attacker as though they were authorized by
   the user.

   Instead of using cookies for authorization, server operators might
   wish to consider entangling designation and authorization by treating
   URLs as capabilities.  Instead of storing secrets in cookies, this
   approach stores secrets in URLs, requiring the remote entity to
   supply the secret itself.  Although this approach is not a panacea,
   judicious application of these principles can lead to more robust
   security.

## 8.3.  Clear Text

   Unless sent over a secure channel (such as TLS), the information in
   the Cookie and Set-Cookie headers is transmitted in the clear.

   1.  All sensitive information conveyed in these headers is exposed to
       an eavesdropper.

   2.  A malicious intermediary could alter the headers as they travel
       in either direction, with unpredictable results.

   3.  A malicious client could alter the Cookie header before
       transmission, with unpredictable results.






> **SHOULD**: Servers SHOULD encrypt and sign the contents of cookies (using
   whatever format the server desires) when transmitting them to the
   user agent (even when sending the cookies over a secure channel).
   However, encrypting and signing cookie contents does not prevent an
   attacker from transplanting a cookie from one user agent to another
   or from replaying the cookie at a later time.

   In addition to encrypting and signing the contents of every cookie,
> **SHOULD**: servers that require a higher level of security SHOULD use the Cookie
   and Set-Cookie headers only over a secure channel.  When using
> **SHOULD**: cookies over a secure channel, servers SHOULD set the Secure
   attribute (see Section 4.1.2.5) for every cookie.  If a server does
   not set the Secure attribute, the protection provided by the secure
   channel will be largely moot.

   For example, consider a webmail server that stores a session
   identifier in a cookie and is typically accessed over HTTPS.  If the
   server does not set the Secure attribute on its cookies, an active
   network attacker can intercept any outbound HTTP request from the
   user agent and redirect that request to the webmail server over HTTP.
   Even if the webmail server is not listening for HTTP connections, the
   user agent will still include cookies in the request.  The active
   network attacker can intercept these cookies, replay them against the
   server, and learn the contents of the user's email.  If, instead, the
   server had set the Secure attribute on its cookies, the user agent
   would not have included the cookies in the clear-text request.

## 8.4.  Session Identifiers

   Instead of storing session information directly in a cookie (where it
   might be exposed to or replayed by an attacker), servers commonly
   store a nonce (or "session identifier") in a cookie.  When the server
   receives an HTTP request with a nonce, the server can look up state
   information associated with the cookie using the nonce as a key.

   Using session identifier cookies limits the damage an attacker can
   cause if the attacker learns the contents of a cookie because the
   nonce is useful only for interacting with the server (unlike non-
   nonce cookie content, which might itself be sensitive).  Furthermore,
   using a single nonce prevents an attacker from "splicing" together
   cookie content from two interactions with the server, which could
   cause the server to behave unexpectedly.

   Using session identifiers is not without risk.  For example, the
> **SHOULD**: server SHOULD take care to avoid "session fixation" vulnerabilities.
   A session fixation attack proceeds in three steps.  First, the
   attacker transplants a session identifier from his or her user agent
   to the victim's user agent.  Second, the victim uses that session



   identifier to interact with the server, possibly imbuing the session
   identifier with the user's credentials or confidential information.
   Third, the attacker uses the session identifier to interact with
   server directly, possibly obtaining the user's authority or
   confidential information.

## 8.5.  Weak Confidentiality

   Cookies do not provide isolation by port.  If a cookie is readable by
   a service running on one port, the cookie is also readable by a
   service running on another port of the same server.  If a cookie is
   writable by a service on one port, the cookie is also writable by a
   service running on another port of the same server.  For this reason,
> **SHOULD NOT**: servers SHOULD NOT both run mutually distrusting services on
   different ports of the same host and use cookies to store security-
   sensitive information.

   Cookies do not provide isolation by scheme.  Although most commonly
   used with the http and https schemes, the cookies for a given host
   might also be available to other schemes, such as ftp and gopher.
   Although this lack of isolation by scheme is most apparent in non-
   HTTP APIs that permit access to cookies (e.g., HTML's document.cookie
   API), the lack of isolation by scheme is actually present in
   requirements for processing cookies themselves (e.g., consider
   retrieving a URI with the gopher scheme via HTTP).

   Cookies do not always provide isolation by path.  Although the
   network-level protocol does not send cookies stored for one path to
   another, some user agents expose cookies via non-HTTP APIs, such as
   HTML's document.cookie API.  Because some of these user agents (e.g.,
   web browsers) do not isolate resources received from different paths,
   a resource retrieved from one path might be able to access cookies
   stored for another path.

## 8.6.  Weak Integrity

   Cookies do not provide integrity guarantees for sibling domains (and
   their subdomains).  For example, consider foo.example.com and
   bar.example.com.  The foo.example.com server can set a cookie with a
   Domain attribute of "example.com" (possibly overwriting an existing
   "example.com" cookie set by bar.example.com), and the user agent will
   include that cookie in HTTP requests to bar.example.com.  In the
   worst case, bar.example.com will be unable to distinguish this cookie
   from a cookie it set itself.  The foo.example.com server might be
   able to leverage this ability to mount an attack against
   bar.example.com.





   Even though the Set-Cookie header supports the Path attribute, the
   Path attribute does not provide any integrity protection because the
   user agent will accept an arbitrary Path attribute in a Set-Cookie
   header.  For example, an HTTP response to a request for
   http://example.com/foo/bar can set a cookie with a Path attribute of
> **SHOULD NOT**: "/qux".  Consequently, servers SHOULD NOT both run mutually
   distrusting services on different paths of the same host and use
   cookies to store security-sensitive information.

   An active network attacker can also inject cookies into the Cookie
   header sent to https://example.com/ by impersonating a response from
   http://example.com/ and injecting a Set-Cookie header.  The HTTPS
   server at example.com will be unable to distinguish these cookies
   from cookies that it set itself in an HTTPS response.  An active
   network attacker might be able to leverage this ability to mount an
   attack against example.com even if example.com uses HTTPS
   exclusively.

   Servers can partially mitigate these attacks by encrypting and
   signing the contents of their cookies.  However, using cryptography
   does not mitigate the issue completely because an attacker can replay
   a cookie he or she received from the authentic example.com server in
   the user's session, with unpredictable results.

   Finally, an attacker might be able to force the user agent to delete
   cookies by storing a large number of cookies.  Once the user agent
   reaches its storage limit, the user agent will be forced to evict
> **SHOULD NOT**: some cookies.  Servers SHOULD NOT rely upon user agents retaining
   cookies.

## 8.7.  Reliance on DNS

   Cookies rely upon the Domain Name System (DNS) for security.  If the
   DNS is partially or fully compromised, the cookie protocol might fail
   to provide the security properties required by applications.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
