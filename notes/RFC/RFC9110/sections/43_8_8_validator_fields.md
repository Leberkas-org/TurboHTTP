---
title: "8.8.  Validator Fields"
rfc_number: 9110
rfc_section: "8.8"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 8.8: Validator Fields — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, validator_fields]
---

## 8.8.  Validator Fields

## 8.8  Validator Fields

   Resource metadata is referred to as a "validator" if it can be used
   within a precondition (Section 13.1) to make a conditional request
   (Section 13).  Validator fields convey a current validator for the
   selected representation (Section 3.2).

   In responses to safe requests, validator fields describe the selected
   representation chosen by the origin server while handling the
   response.  Note that, depending on the method and status code
   semantics, the selected representation for a given response is not
   necessarily the same as the representation enclosed as response
   content.

   In a successful response to a state-changing request, validator
   fields describe the new representation that has replaced the prior
   selected representation as a result of processing the request.

   For example, an ETag field in a 201 (Created) response communicates
   the entity tag of the newly created resource's representation, so
   that the entity tag can be used as a validator in later conditional
   requests to prevent the "lost update" problem.

   This specification defines two forms of metadata that are commonly
   used to observe resource state and test for preconditions:
   modification dates (Section 8.8.2) and opaque entity tags
   (Section 8.8.3).  Additional metadata that reflects resource state
   has been defined by various extensions of HTTP, such as Web
   Distributed Authoring and Versioning [WEBDAV], that are beyond the
   scope of this specification.

### 8.8.1  Weak versus Strong

   Validators come in two flavors: strong or weak.  Weak validators are
   easy to generate but are far less useful for comparisons.  Strong
   validators are ideal for comparisons but can be very difficult (and
   occasionally impossible) to generate efficiently.  Rather than impose
   that all forms of resource adhere to the same strength of validator,
   HTTP exposes the type of validator in use and imposes restrictions on
   when weak validators can be used as preconditions.

   A "strong validator" is representation metadata that changes value
   whenever a change occurs to the representation data that would be
   observable in the content of a 200 (OK) response to GET.

   A strong validator might change for reasons other than a change to
   the representation data, such as when a semantically significant part
   of the representation metadata is changed (e.g., Content-Type), but
   it is in the best interests of the origin server to only change the
   value when it is necessary to invalidate the stored responses held by
   remote caches and authoring tools.

   Cache entries might persist for arbitrarily long periods, regardless
   of expiration times.  Thus, a cache might attempt to validate an
   entry using a validator that it obtained in the distant past.  A
   strong validator is unique across all versions of all representations
   associated with a particular resource over time.  However, there is
   no implication of uniqueness across representations of different
   resources (i.e., the same strong validator might be in use for
   representations of multiple resources at the same time and does not
   imply that those representations are equivalent).

   There are a variety of strong validators used in practice.  The best
   are based on strict revision control, wherein each change to a
   representation always results in a unique node name and revision
   identifier being assigned before the representation is made
   accessible to GET.  A collision-resistant hash function applied to
   the representation data is also sufficient if the data is available
   prior to the response header fields being sent and the digest does
   not need to be recalculated every time a validation request is
   received.  However, if a resource has distinct representations that
   differ only in their metadata, such as might occur with content
   negotiation over media types that happen to share the same data
   format, then the origin server needs to incorporate additional
   information in the validator to distinguish those representations.

   In contrast, a "weak validator" is representation metadata that might
   not change for every change to the representation data.  This
   weakness might be due to limitations in how the value is calculated
   (e.g., clock resolution), an inability to ensure uniqueness for all
   possible representations of the resource, or a desire of the resource
   owner to group representations by some self-determined set of
   equivalency rather than unique sequences of data.

> **SHOULD**: An origin server SHOULD change a weak entity tag whenever it
   considers prior representations to be unacceptable as a substitute
   for the current representation.  In other words, a weak entity tag
   ought to change whenever the origin server wants caches to invalidate
   old responses.

   For example, the representation of a weather report that changes in
   content every second, based on dynamic measurements, might be grouped
   into sets of equivalent representations (from the origin server's
   perspective) with the same weak validator in order to allow cached
   representations to be valid for a reasonable period of time (perhaps
   adjusted dynamically based on server load or weather quality).
   Likewise, a representation's modification time, if defined with only
   one-second resolution, might be a weak validator if it is possible
   for the representation to be modified twice during a single second
   and retrieved between those modifications.

   Likewise, a validator is weak if it is shared by two or more
   representations of a given resource at the same time, unless those
   representations have identical representation data.  For example, if
   the origin server sends the same validator for a representation with
   a gzip content coding applied as it does for a representation with no
   content coding, then that validator is weak.  However, two
   simultaneous representations might share the same strong validator if
   they differ only in the representation metadata, such as when two
   different media types are available for the same representation data.

   Strong validators are usable for all conditional requests, including
   cache validation, partial content ranges, and "lost update"
   avoidance.  Weak validators are only usable when the client does not
   require exact equality with previously obtained representation data,
   such as when validating a cache entry or limiting a web traversal to
   recent changes.

### 8.8.2  Last-Modified

   The "Last-Modified" header field in a response provides a timestamp
   indicating the date and time at which the origin server believes the
   selected representation was last modified, as determined at the
   conclusion of handling the request.


```abnf
     Last-Modified = HTTP-date
```


   An example of its use is

   Last-Modified: Tue, 15 Nov 1994 12:45:26 GMT

#### 8.8.2.1  Generation

> **SHOULD**: An origin server SHOULD send Last-Modified for any selected
   representation for which a last modification date can be reasonably
   and consistently determined, since its use in conditional requests
   and evaluating cache freshness ([CACHING]) can substantially reduce
   unnecessary transfers and significantly improve service availability
   and scalability.

   A representation is typically the sum of many parts behind the
   resource interface.  The last-modified time would usually be the most
   recent time that any of those parts were changed.  How that value is
   determined for any given resource is an implementation detail beyond
   the scope of this specification.

> **SHOULD**: An origin server SHOULD obtain the Last-Modified value of the
   representation as close as possible to the time that it generates the
   Date field value for its response.  This allows a recipient to make
   an accurate assessment of the representation's modification time,
   especially if the representation changes near the time that the
   response is generated.

> **MUST NOT**: An origin server with a clock (as defined in Section 5.6.7) MUST NOT
   generate a Last-Modified date that is later than the server's time of
   message origination (Date, Section 6.6.1).  If the last modification
   time is derived from implementation-specific metadata that evaluates
   to some time in the future, according to the origin server's clock,
> **MUST**: then the origin server MUST replace that value with the message
   origination date.  This prevents a future modification date from
   having an adverse impact on cache validation.

> **MUST NOT**: An origin server without a clock MUST NOT generate a Last-Modified
   date for a response unless that date value was assigned to the
   resource by some other system (presumably one with a clock).

#### 8.8.2.2  Comparison

   A Last-Modified time, when used as a validator in a request, is
   implicitly weak unless it is possible to deduce that it is strong,
   using the following rules:

   *  The validator is being compared by an origin server to the actual
      current validator for the representation and,

   *  That origin server reliably knows that the associated
      representation did not change twice during the second covered by
      the presented validator;

   or

   *  The validator is about to be used by a client in an
      If-Modified-Since, If-Unmodified-Since, or If-Range header field,
      because the client has a cache entry for the associated
      representation, and

   *  That cache entry includes a Date value which is at least one
      second after the Last-Modified value and the client has reason to
      believe that they were generated by the same clock or that there
      is enough difference between the Last-Modified and Date values to
      make clock synchronization issues unlikely;

   or

   *  The validator is being compared by an intermediate cache to the
      validator stored in its cache entry for the representation, and

   *  That cache entry includes a Date value which is at least one
      second after the Last-Modified value and the cache has reason to
      believe that they were generated by the same clock or that there
      is enough difference between the Last-Modified and Date values to
      make clock synchronization issues unlikely.

   This method relies on the fact that if two different responses were
   sent by the origin server during the same second, but both had the
   same Last-Modified time, then at least one of those responses would
   have a Date value equal to its Last-Modified time.

### 8.8.3  ETag

   The "ETag" field in a response provides the current entity tag for
   the selected representation, as determined at the conclusion of
   handling the request.  An entity tag is an opaque validator for
   differentiating between multiple representations of the same
   resource, regardless of whether those multiple representations are
   due to resource state changes over time, content negotiation
   resulting in multiple representations being valid at the same time,
   or both.  An entity tag consists of an opaque quoted string, possibly
   prefixed by a weakness indicator.


```abnf
     ETag       = entity-tag

     entity-tag = [ weak ] opaque-tag
     weak       = %s"W/"
     opaque-tag = DQUOTE *etagc DQUOTE
     etagc      = %x21 / %x23-7E / obs-text
                ; VCHAR except double quotes, plus obs-text
```


      |  *Note:* Previously, opaque-tag was defined to be a quoted-
      |  string ([RFC2616], Section 3.11); thus, some recipients might
      |  perform backslash unescaping.  Servers therefore ought to avoid
      |  backslash characters in entity tags.

   An entity tag can be more reliable for validation than a modification
   date in situations where it is inconvenient to store modification
   dates, where the one-second resolution of HTTP-date values is not
   sufficient, or where modification dates are not consistently
   maintained.

   Examples:

   ETag: "xyzzy"
   ETag: W/"xyzzy"
   ETag: ""

   An entity tag can be either a weak or strong validator, with strong
   being the default.  If an origin server provides an entity tag for a
   representation and the generation of that entity tag does not satisfy
   all of the characteristics of a strong validator (Section 8.8.1),
> **MUST**: then the origin server MUST mark the entity tag as weak by prefixing
   its opaque value with "W/" (case-sensitive).

> **MAY**: A sender MAY send the ETag field in a trailer section (see
   Section 6.5).  However, since trailers are often ignored, it is
   preferable to send ETag as a header field unless the entity tag is
   generated while sending the content.

#### 8.8.3.1  Generation

   The principle behind entity tags is that only the service author
   knows the implementation of a resource well enough to select the most
   accurate and efficient validation mechanism for that resource, and
   that any such mechanism can be mapped to a simple sequence of octets
   for easy comparison.  Since the value is opaque, there is no need for
   the client to be aware of how each entity tag is constructed.

   For example, a resource that has implementation-specific versioning
   applied to all changes might use an internal revision number, perhaps
   combined with a variance identifier for content negotiation, to
   accurately differentiate between representations.  Other
   implementations might use a collision-resistant hash of
   representation content, a combination of various file attributes, or
   a modification timestamp that has sub-second resolution.

> **SHOULD**: An origin server SHOULD send an ETag for any selected representation
   for which detection of changes can be reasonably and consistently
   determined, since the entity tag's use in conditional requests and
   evaluating cache freshness ([CACHING]) can substantially reduce
   unnecessary transfers and significantly improve service availability,
   scalability, and reliability.

#### 8.8.3.2  Comparison

   There are two entity tag comparison functions, depending on whether
   or not the comparison context allows the use of weak validators:

   "Strong comparison":  two entity tags are equivalent if both are not
      weak and their opaque-tags match character-by-character.

   "Weak comparison":  two entity tags are equivalent if their opaque-
      tags match character-by-character, regardless of either or both
      being tagged as "weak".

   The example below shows the results for a set of entity tag pairs and
   both the weak and strong comparison function results:

   +========+========+===================+=================+
   | ETag 1 | ETag 2 | Strong Comparison | Weak Comparison |
   +========+========+===================+=================+
   | W/"1"  | W/"1"  | no match          | match           |
   +--------+--------+-------------------+-----------------+
   | W/"1"  | W/"2"  | no match          | no match        |
   +--------+--------+-------------------+-----------------+
   | W/"1"  | "1"    | no match          | match           |
   +--------+--------+-------------------+-----------------+
   | "1"    | "1"    | match             | match           |
   +--------+--------+-------------------+-----------------+

                            Table 3

#### 8.8.3.3  Example: Entity Tags Varying on Content-Negotiated Resources

   Consider a resource that is subject to content negotiation
   (Section 12), and where the representations sent in response to a GET
   request vary based on the Accept-Encoding request header field
   (Section 12.5.3):

   >> Request:

   GET /index HTTP/1.1
   Host: www.example.com
   Accept-Encoding: gzip

   In this case, the response might or might not use the gzip content
   coding.  If it does not, the response might look like:

   >> Response:

   HTTP/1.1 200 OK
   Date: Fri, 26 Mar 2010 00:05:00 GMT
   ETag: "123-a"
   Content-Length: 70
   Vary: Accept-Encoding
   Content-Type: text/plain

   Hello World!
   Hello World!
   Hello World!
   Hello World!
   Hello World!

   An alternative representation that does use gzip content coding would
   be:

   >> Response:

   HTTP/1.1 200 OK
   Date: Fri, 26 Mar 2010 00:05:00 GMT
   ETag: "123-b"
   Content-Length: 43
   Vary: Accept-Encoding
   Content-Type: text/plain
   Content-Encoding: gzip

   ...binary data...

      |  *Note:* Content codings are a property of the representation
      |  data, so a strong entity tag for a content-encoded
      |  representation has to be distinct from the entity tag of an
      |  unencoded representation to prevent potential conflicts during
      |  cache updates and range requests.  In contrast, transfer
      |  codings (Section 7 of [HTTP/1.1]) apply only during message
      |  transfer and do not result in distinct entity tags.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
