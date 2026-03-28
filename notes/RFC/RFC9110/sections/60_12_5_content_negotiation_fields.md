---
title: "12.5.  Content Negotiation Fields"
rfc_number: 9110
rfc_section: "12.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 12.5: Content Negotiation Fields — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, content_negotiation_fields]
---

## 12.5.  Content Negotiation Fields

## 12.5  Content Negotiation Fields

### 12.5.1  Accept

   The "Accept" header field can be used by user agents to specify their
   preferences regarding response media types.  For example, Accept
   header fields can be used to indicate that the request is
   specifically limited to a small set of desired types, as in the case
   of a request for an in-line image.

   When sent by a server in a response, Accept provides information
   about which content types are preferred in the content of a
   subsequent request to the same resource.


```abnf
     Accept = #( media-range [ weight ] )

     media-range    = ( "*/*"
                        / ( type "/" "*" )
                        / ( type "/" subtype )
                      ) parameters
```


   The asterisk "*" character is used to group media types into ranges,
   with "*/*" indicating all media types and "type/*" indicating all
   subtypes of that type.  The media-range can include media type
   parameters that are applicable to that range.

   Each media-range might be followed by optional applicable media type
   parameters (e.g., charset), followed by an optional "q" parameter for
   indicating a relative weight (Section 12.4.2).

   Previous specifications allowed additional extension parameters to
   appear after the weight parameter.  The accept extension grammar
   (accept-params, accept-ext) has been removed because it had a
   complicated definition, was not being used in practice, and is more
   easily deployed through new header fields.  Senders using weights
> **SHOULD**: SHOULD send "q" last (after all media-range parameters).  Recipients
   SHOULD process any parameter named "q" as weight, regardless of
   parameter ordering.

      |  *Note:* Use of the "q" parameter name to control content
      |  negotiation would interfere with any media type parameter
      |  having the same name.  Hence, the media type registry disallows
      |  parameters named "q".

   The example

   Accept: audio/*; q=0.2, audio/basic

   is interpreted as "I prefer audio/basic, but send me any audio type
   if it is the best available after an 80% markdown in quality".

   A more elaborate example is

   Accept: text/plain; q=0.5, text/html,
          text/x-dvi; q=0.8, text/x-c

   Verbally, this would be interpreted as "text/html and text/x-c are
   the equally preferred media types, but if they do not exist, then
   send the text/x-dvi representation, and if that does not exist, send
   the text/plain representation".

   Media ranges can be overridden by more specific media ranges or
   specific media types.  If more than one media range applies to a
   given type, the most specific reference has precedence.  For example,

   Accept: text/*, text/plain, text/plain;format=flowed, */*

   have the following precedence:

   1.  text/plain;format=flowed

   2.  text/plain

   3.  text/*

   4.  */*

   The media type quality factor associated with a given type is
   determined by finding the media range with the highest precedence
   that matches the type.  For example,

   Accept: text/*;q=0.3, text/plain;q=0.7, text/plain;format=flowed,
          text/plain;format=fixed;q=0.4, */*;q=0.5

   would cause the following values to be associated:

   +==========================+===============+
   | Media Type               | Quality Value |
   +==========================+===============+
   | text/plain;format=flowed | 1             |
   +--------------------------+---------------+
   | text/plain               | 0.7           |
   +--------------------------+---------------+
   | text/html                | 0.3           |
   +--------------------------+---------------+
   | image/jpeg               | 0.5           |
   +--------------------------+---------------+
   | text/plain;format=fixed  | 0.4           |
   +--------------------------+---------------+
   | text/html;level=3        | 0.7           |
   +--------------------------+---------------+

                     Table 5

      |  *Note:* A user agent might be provided with a default set of
      |  quality values for certain media ranges.  However, unless the
      |  user agent is a closed system that cannot interact with other
      |  rendering agents, this default set ought to be configurable by
      |  the user.

### 12.5.2  Accept-Charset

   The "Accept-Charset" header field can be sent by a user agent to
   indicate its preferences for charsets in textual response content.
   For example, this field allows user agents capable of understanding
   more comprehensive or special-purpose charsets to signal that
   capability to an origin server that is capable of representing
   information in those charsets.


```abnf
     Accept-Charset = #( ( token / "*" ) [ weight ] )
```


> **MAY**: Charset names are defined in Section 8.3.2.  A user agent MAY
   associate a quality value with each charset to indicate the user's
   relative preference for that charset, as defined in Section 12.4.2.
   An example is

   Accept-Charset: iso-8859-5, unicode-1-1;q=0.8

   The special value "*", if present in the Accept-Charset header field,
   matches every charset that is not mentioned elsewhere in the field.

      |  *Note:* Accept-Charset is deprecated because UTF-8 has become
      |  nearly ubiquitous and sending a detailed list of user-preferred
      |  charsets wastes bandwidth, increases latency, and makes passive
      |  fingerprinting far too easy (Section 17.13).  Most general-
      |  purpose user agents do not send Accept-Charset unless
      |  specifically configured to do so.

### 12.5.3  Accept-Encoding

   The "Accept-Encoding" header field can be used to indicate
   preferences regarding the use of content codings (Section 8.4.1).

   When sent by a user agent in a request, Accept-Encoding indicates the
   content codings acceptable in a response.

   When sent by a server in a response, Accept-Encoding provides
   information about which content codings are preferred in the content
   of a subsequent request to the same resource.

   An "identity" token is used as a synonym for "no encoding" in order
   to communicate when no encoding is preferred.


```abnf
     Accept-Encoding  = #( codings [ weight ] )
     codings          = content-coding / "identity" / "*"
```


> **MAY**: Each codings value MAY be given an associated quality value (weight)
   representing the preference for that encoding, as defined in
   Section 12.4.2.  The asterisk "*" symbol in an Accept-Encoding field
   matches any available content coding not explicitly listed in the
   field.

   Examples:

   Accept-Encoding: compress, gzip
   Accept-Encoding:
   Accept-Encoding: *
   Accept-Encoding: compress;q=0.5, gzip;q=1.0
   Accept-Encoding: gzip;q=1.0, identity; q=0.5, *;q=0

   A server tests whether a content coding for a given representation is
   acceptable using these rules:

   1.  If no Accept-Encoding header field is in the request, any content
       coding is considered acceptable by the user agent.

   2.  If the representation has no content coding, then it is
       acceptable by default unless specifically excluded by the Accept-
       Encoding header field stating either "identity;q=0" or "*;q=0"
       without a more specific entry for "identity".

   3.  If the representation's content coding is one of the content
       codings listed in the Accept-Encoding field value, then it is
       acceptable unless it is accompanied by a qvalue of 0.  (As
       defined in Section 12.4.2, a qvalue of 0 means "not acceptable".)

   A representation could be encoded with multiple content codings.
   However, most content codings are alternative ways to accomplish the
   same purpose (e.g., data compression).  When selecting between
   multiple content codings that have the same purpose, the acceptable
   content coding with the highest non-zero qvalue is preferred.

   An Accept-Encoding header field with a field value that is empty
   implies that the user agent does not want any content coding in
   response.  If a non-empty Accept-Encoding header field is present in
   a request and none of the available representations for the response
   have a content coding that is listed as acceptable, the origin server
> **SHOULD**: SHOULD send a response without any content coding unless the identity
   coding is indicated as unacceptable.

   When the Accept-Encoding header field is present in a response, it
   indicates what content codings the resource was willing to accept in
   the associated request.  The field value is evaluated the same way as
   in a request.

   Note that this information is specific to the associated request; the
   set of supported encodings might be different for other resources on
   the same server and could change over time or depend on other aspects
   of the request (such as the request method).

   Servers that fail a request due to an unsupported content coding
   ought to respond with a 415 (Unsupported Media Type) status and
   include an Accept-Encoding header field in that response, allowing
   clients to distinguish between issues related to content codings and
   media types.  In order to avoid confusion with issues related to
   media types, servers that fail a request with a 415 status for
> **MUST NOT**: reasons unrelated to content codings MUST NOT include the Accept-
   Encoding header field.

   The most common use of Accept-Encoding is in responses with a 415
   (Unsupported Media Type) status code, in response to optimistic use
   of a content coding by clients.  However, the header field can also
   be used to indicate to clients that content codings are supported in
   order to optimize future interactions.  For example, a resource might
   include it in a 2xx (Successful) response when the request content
   was big enough to justify use of a compression coding but the client
   failed do so.

### 12.5.4  Accept-Language

   The "Accept-Language" header field can be used by user agents to
   indicate the set of natural languages that are preferred in the
   response.  Language tags are defined in Section 8.5.1.


```abnf
     Accept-Language = #( language-range [ weight ] )
```

     language-range  =
               <language-range, see [RFC4647], Section 2.1>

   Each language-range can be given an associated quality value
   representing an estimate of the user's preference for the languages
   specified by that range, as defined in Section 12.4.2.  For example,

   Accept-Language: da, en-gb;q=0.8, en;q=0.7

   would mean: "I prefer Danish, but will accept British English and
   other types of English".

   Note that some recipients treat the order in which language tags are
   listed as an indication of descending priority, particularly for tags
   that are assigned equal quality values (no value is the same as q=1).
   However, this behavior cannot be relied upon.  For consistency and to
   maximize interoperability, many user agents assign each language tag
   a unique quality value while also listing them in order of decreasing
   quality.  Additional discussion of language priority lists can be
   found in Section 2.3 of [RFC4647].

   For matching, Section 3 of [RFC4647] defines several matching
   schemes.  Implementations can offer the most appropriate matching
   scheme for their requirements.  The "Basic Filtering" scheme
   ([RFC4647], Section 3.3.1) is identical to the matching scheme that
   was previously defined for HTTP in Section 14.4 of [RFC2616].

   It might be contrary to the privacy expectations of the user to send
   an Accept-Language header field with the complete linguistic
   preferences of the user in every request (Section 17.13).

   Since intelligibility is highly dependent on the individual user,
   user agents need to allow user control over the linguistic preference
   (either through configuration of the user agent itself or by
   defaulting to a user controllable system setting).  A user agent that
> **MUST NOT**: does not provide such control to the user MUST NOT send an Accept-
   Language header field.

      |  *Note:* User agents ought to provide guidance to users when
      |  setting a preference, since users are rarely familiar with the
      |  details of language matching as described above.  For example,
      |  users might assume that on selecting "en-gb", they will be
      |  served any kind of English document if British English is not
      |  available.  A user agent might suggest, in such a case, to add
      |  "en" to the list for better matching behavior.

### 12.5.5  Vary

   The "Vary" header field in a response describes what parts of a
   request message, aside from the method and target URI, might have
   influenced the origin server's process for selecting the content of
   this response.


```abnf
     Vary = #( "*" / field-name )
```


   A Vary field value is either the wildcard member "*" or a list of
   request field names, known as the selecting header fields, that might
   have had a role in selecting the representation for this response.
   Potential selecting header fields are not limited to fields defined
   by this specification.

   A list containing the member "*" signals that other aspects of the
   request might have played a role in selecting the response
   representation, possibly including aspects outside the message syntax
   (e.g., the client's network address).  A recipient will not be able
   to determine whether this response is appropriate for a later request
> **MUST**: without forwarding the request to the origin server.  A proxy MUST
   NOT generate "*" in a Vary field value.

   For example, a response that contains

   Vary: accept-encoding, accept-language

   indicates that the origin server might have used the request's
   Accept-Encoding and Accept-Language header fields (or lack thereof)
   as determining factors while choosing the content for this response.

   A Vary field containing a list of field names has two purposes:

> **MUST NOT**: 1.  To inform cache recipients that they MUST NOT use this response
       to satisfy a later request unless the later request has the same
       values for the listed header fields as the original request
       (Section 4.1 of [CACHING]) or reuse of the response has been
       validated by the origin server.  In other words, Vary expands the
       cache key required to match a new request to the stored cache
       entry.

   2.  To inform user agent recipients that this response was subject to
       content negotiation (Section 12) and a different representation
       might be sent in a subsequent request if other values are
       provided in the listed header fields (proactive negotiation).

> **SHOULD**: An origin server SHOULD generate a Vary header field on a cacheable
   response when it wishes that response to be selectively reused for
   subsequent requests.  Generally, that is the case when the response
   content has been tailored to better fit the preferences expressed by
   those selecting header fields, such as when an origin server has
   selected the response's language based on the request's
   Accept-Language header field.

   Vary might be elided when an origin server considers variance in
   content selection to be less significant than Vary's performance
   impact on caching, particularly when reuse is already limited by
   cache response directives (Section 5.2 of [CACHING]).

   There is no need to send the Authorization field name in Vary because
   reuse of that response for a different user is prohibited by the
   field definition (Section 11.6.2).  Likewise, if the response content
   has been selected or influenced by network region, but the origin
   server wants the cached response to be reused even if recipients move
   from one region to another, then there is no need for the origin
   server to indicate such variance in Vary.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
