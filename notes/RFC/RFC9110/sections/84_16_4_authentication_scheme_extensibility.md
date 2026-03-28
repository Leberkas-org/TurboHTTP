---
title: "16.4.  Authentication Scheme Extensibility"
rfc_number: 9110
rfc_section: "16.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 16.4: Authentication Scheme Extensibility — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, authentication_scheme_extensibility]
---

## 16.4.  Authentication Scheme Extensibility

## 16.4  Authentication Scheme Extensibility

### 16.4.1  Authentication Scheme Registry

   The "Hypertext Transfer Protocol (HTTP) Authentication Scheme
   Registry" defines the namespace for the authentication schemes in
   challenges and credentials.  It is maintained at
   <https://www.iana.org/assignments/http-authschemes>.

> **MUST**: Registrations MUST include the following fields:

   *  Authentication Scheme Name

   *  Pointer to specification text

   *  Notes (optional)

   Values to be added to this namespace require IETF Review (see
   [RFC8126], Section 4.8).

### 16.4.2  Considerations for New Authentication Schemes

   There are certain aspects of the HTTP Authentication framework that
   put constraints on how new authentication schemes can work:

   *  HTTP authentication is presumed to be stateless: all of the
> **MUST**: information necessary to authenticate a request MUST be provided
      in the request, rather than be dependent on the server remembering
      prior requests.  Authentication based on, or bound to, the
      underlying connection is outside the scope of this specification
      and inherently flawed unless steps are taken to ensure that the
      connection cannot be used by any party other than the
      authenticated user (see Section 3.3).

   *  The authentication parameter "realm" is reserved for defining
> **MUST**: protection spaces as described in Section 11.5.  New schemes MUST
      NOT use it in a way incompatible with that definition.

   *  The "token68" notation was introduced for compatibility with
      existing authentication schemes and can only be used once per
      challenge or credential.  Thus, new schemes ought to use the auth-
      param syntax instead, because otherwise future extensions will be
      impossible.

   *  The parsing of challenges and credentials is defined by this
      specification and cannot be modified by new authentication
      schemes.  When the auth-param syntax is used, all parameters ought
      to support both token and quoted-string syntax, and syntactical
      constraints ought to be defined on the field value after parsing
      (i.e., quoted-string processing).  This is necessary so that
      recipients can use a generic parser that applies to all
      authentication schemes.

      *Note:* The fact that the value syntax for the "realm" parameter
      is restricted to quoted-string was a bad design choice not to be
      repeated for new parameters.

   *  Definitions of new schemes ought to define the treatment of
      unknown extension parameters.  In general, a "must-ignore" rule is
      preferable to a "must-understand" rule, because otherwise it will
      be hard to introduce new parameters in the presence of legacy
      recipients.  Furthermore, it's good to describe the policy for
      defining new parameters (such as "update the specification" or
      "use this registry").

   *  Authentication schemes need to document whether they are usable in
      origin-server authentication (i.e., using WWW-Authenticate), and/
      or proxy authentication (i.e., using Proxy-Authenticate).

   *  The credentials carried in an Authorization header field are
      specific to the user agent and, therefore, have the same effect on
      HTTP caches as the "private" cache response directive
      (Section 5.2.2.7 of [CACHING]), within the scope of the request in
      which they appear.

      Therefore, new authentication schemes that choose not to carry
      credentials in the Authorization header field (e.g., using a newly
      defined header field) will need to explicitly disallow caching, by
      mandating the use of cache response directives (e.g., "private").

   *  Schemes using Authentication-Info, Proxy-Authentication-Info, or
      any other authentication related response header field need to
      consider and document the related security considerations (see
      Section 17.16.4).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
