---
title: "Appendix B.  Changes from Previous RFCs"
rfc_number: 9110
rfc_section: "Appendix B"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Appendix B: Changes from Previous RFCs — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, changes_from_previous_rfcs]
---

## Appendix B.  Changes from Previous RFCs

Appendix B.  Changes from Previous RFCs

B.1.  Changes from RFC 2818

   None.

B.2.  Changes from RFC 7230

   The sections introducing HTTP's design goals, history, architecture,
   conformance criteria, protocol versioning, URIs, message routing, and
   header fields have been moved here.

   The requirement on semantic conformance has been replaced with
   permission to ignore or work around implementation-specific failures.
   (Section 2.2)

   The description of an origin and authoritative access to origin
   servers has been extended for both "http" and "https" URIs to account
   for alternative services and secured connections that are not
   necessarily based on TCP.  (Sections 4.2.1, 4.2.2, 4.3.1, and 7.3.3)

   Explicit requirements have been added to check the target URI
   scheme's semantics and reject requests that don't meet any associated
   requirements.  (Section 7.4)

   Parameters in media type, media range, and expectation can be empty
   via one or more trailing semicolons.  (Section 5.6.6)

   "Field value" now refers to the value after multiple field lines are
   combined with commas -- by far the most common use.  To refer to a
   single header line's value, use "field line value".  (Section 6.3)

   Trailer field semantics now transcend the specifics of chunked
   transfer coding.  The use of trailer fields has been further limited
   to allow generation as a trailer field only when the sender knows the
   field defines that usage and to allow merging into the header section
   only if the recipient knows the corresponding field definition
   permits and defines how to merge.  In all other cases,
   implementations are encouraged either to store the trailer fields
   separately or to discard them instead of merging.  (Section 6.5.1)

   The priority of the absolute form of the request URI over the Host
   header field by origin servers has been made explicit to align with
   proxy handling.  (Section 7.2)

   The grammar definition for the Via field's "received-by" was expanded
   in RFC 7230 due to changes in the URI grammar for host [URI] that are
   not desirable for Via. For simplicity, we have removed uri-host from
   the received-by production because it can be encompassed by the
   existing grammar for pseudonym.  In particular, this change removed
   comma from the allowed set of characters for a host name in received-
   by.  (Section 7.6.3)

B.3.  Changes from RFC 7231

   Minimum URI lengths to be supported by implementations are now
   recommended.  (Section 4.1)

   The following have been clarified: CR and NUL in field values are to
   be rejected or mapped to SP, and leading and trailing whitespace
   needs to be stripped from field values before they are consumed.
   (Section 5.5)

   Parameters in media type, media range, and expectation can be empty
   via one or more trailing semicolons.  (Section 5.6.6)

   An abstract data type for HTTP messages has been introduced to define
   the components of a message and their semantics as an abstraction
   across multiple HTTP versions, rather than in terms of the specific
   syntax form of HTTP/1.1 in [HTTP/1.1], and reflect the contents after
   the message is parsed.  This makes it easier to distinguish between
   requirements on the content (what is conveyed) versus requirements on
   the messaging syntax (how it is conveyed) and avoids baking
   limitations of early protocol versions into the future of HTTP.
   (Section 6)

   The terms "payload" and "payload body" have been replaced with
   "content", to better align with its usage elsewhere (e.g., in field
   names) and to avoid confusion with frame payloads in HTTP/2 and
   HTTP/3.  (Section 6.4)

   The term "effective request URI" has been replaced with "target URI".
   (Section 7.1)

   Restrictions on client retries have been loosened to reflect
   implementation behavior.  (Section 9.2.2)

   The fact that request bodies on GET, HEAD, and DELETE are not
   interoperable has been clarified.  (Sections 9.3.1, 9.3.2, and 9.3.5)

   The use of the Content-Range header field (Section 14.4) as a request
   modifier on PUT is allowed.  (Section 9.3.4)

   A superfluous requirement about setting Content-Length has been
   removed from the description of the OPTIONS method.  (Section 9.3.7)

   The normative requirement to use the "message/http" media type in
   TRACE responses has been removed.  (Section 9.3.8)

   List-based grammar for Expect has been restored for compatibility
   with RFC 2616.  (Section 10.1.1)

   Accept and Accept-Encoding are allowed in response messages; the
   latter was introduced by [RFC7694].  (Section 12.3)

   "Accept Parameters" (accept-params and accept-ext ABNF production)
   have been removed from the definition of the Accept field.
   (Section 12.5.1)

   The Accept-Charset field is now deprecated.  (Section 12.5.2)

   The semantics of "*" in the Vary header field when other values are
   present was clarified.  (Section 12.5.5)

   Range units are compared in a case-insensitive fashion.
   (Section 14.1)

   The use of the Accept-Ranges field is not restricted to origin
   servers.  (Section 14.3)

   The process of creating a redirected request has been clarified.
   (Section 15.4)

   Status code 308 (previously defined in [RFC7538]) has been added so
   that it's defined closer to status codes 301, 302, and 307.
   (Section 15.4.9)

   Status code 421 (previously defined in Section 9.1.2 of [RFC7540])
   has been added because of its general applicability. 421 is no longer
   defined as heuristically cacheable since the response is specific to
   the connection (not the target resource).  (Section 15.5.20)

   Status code 422 (previously defined in Section 11.2 of [WEBDAV]) has
   been added because of its general applicability.  (Section 15.5.21)

B.4.  Changes from RFC 7232

   Previous revisions of HTTP imposed an arbitrary 60-second limit on
   the determination of whether Last-Modified was a strong validator to
   guard against the possibility that the Date and Last-Modified values
   are generated from different clocks or at somewhat different times
   during the preparation of the response.  This specification has
   relaxed that to allow reasonable discretion.  (Section 8.8.2.2)

   An edge-case requirement on If-Match and If-Unmodified-Since has been
   removed that required a validator not to be sent in a 2xx response if
   validation fails because the change request has already been applied.
   (Sections 13.1.1 and 13.1.4)

   The fact that If-Unmodified-Since does not apply to a resource
   without a concept of modification time has been clarified.
   (Section 13.1.4)

   Preconditions can now be evaluated before the request content is
   processed rather than waiting until the response would otherwise be
   successful.  (Section 13.2)

B.5.  Changes from RFC 7233

   Refactored the range-unit and ranges-specifier grammars to simplify
   and reduce artificial distinctions between bytes and other
   (extension) range units, removing the overlapping grammar of other-
   range-unit by defining range units generically as a token and placing
   extensions within the scope of a range-spec (other-range).  This
   disambiguates the role of list syntax (commas) in all range sets,
   including extension range units, for indicating a range-set of more
   than one range.  Moving the extension grammar into range specifiers
   also allows protocol specific to byte ranges to be specified
   separately.

   It is now possible to define Range handling on extension methods.
   (Section 14.2)

   Described use of the Content-Range header field (Section 14.4) as a
   request modifier to perform a partial PUT.  (Section 14.5)

B.6.  Changes from RFC 7235

   None.

B.7.  Changes from RFC 7538

   None.

B.8.  Changes from RFC 7615

   None.

B.9.  Changes from RFC 7694

   This specification includes the extension defined in [RFC7694] but
   leaves out examples and deployment considerations.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
