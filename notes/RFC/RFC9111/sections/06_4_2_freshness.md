---
title: 4.2.  Freshness
rfc_number: 9111
rfc_section: '4.2'
source_url: 'https://www.rfc-editor.org/rfc/rfc9111'
description: 'Section 4.2: Freshness — RFC 9111 — HTTP Caching'
tags:
  - RFC9111
  - HTTP-caching
  - freshness
  - validation
  - Cache-Control
  - max-age
  - Expires
  - conditional-requests
  - Vary
---

## 4.2.  Freshness

## 4.2  Freshness

   A "fresh" response is one whose age has not yet exceeded its
   freshness lifetime.  Conversely, a "stale" response is one where it
   has.

   A response's "freshness lifetime" is the length of time between its
   generation by the origin server and its expiration time.  An
   "explicit expiration time" is the time at which the origin server
   intends that a stored response can no longer be used by a cache
   without further validation, whereas a "heuristic expiration time" is
   assigned by a cache when no explicit expiration time is available.

   A response's "age" is the time that has passed since it was generated
   by, or successfully validated with, the origin server.

   When a response is fresh, it can be used to satisfy subsequent
   requests without contacting the origin server, thereby improving
   efficiency.

   The primary mechanism for determining freshness is for an origin
   server to provide an explicit expiration time in the future, using
   either the Expires header field (Section 5.3) or the max-age response
   directive (Section 5.2.2.1).  Generally, origin servers will assign
   future explicit expiration times to responses in the belief that the
   representation is not likely to change in a semantically significant
   way before the expiration time is reached.

   If an origin server wishes to force a cache to validate every
   request, it can assign an explicit expiration time in the past to
   indicate that the response is already stale.  Compliant caches will
   normally validate a stale cached response before reusing it for
   subsequent requests (see Section 4.2.4).

   Since origin servers do not always provide explicit expiration times,
   caches are also allowed to use a heuristic to determine an expiration
   time under certain circumstances (see Section 4.2.2).

   The calculation to determine if a response is fresh is:

      response_is_fresh = (freshness_lifetime > current_age)

   freshness_lifetime is defined in Section 4.2.1; current_age is
   defined in Section 4.2.3.

   Clients can send the max-age or min-fresh request directives
   (Section 5.2.1) to suggest limits on the freshness calculations for
   the corresponding response.  However, caches are not required to
   honor them.

   When calculating freshness, to avoid common problems in date parsing:

   *  Although all date formats are specified to be case-sensitive, a
> **SHOULD**: cache recipient SHOULD match the field value case-insensitively.

   *  If a cache recipient's internal implementation of time has less
> **MUST**: resolution than the value of an HTTP-date, the recipient MUST
      internally represent a parsed Expires date as the nearest time
      equal to or earlier than the received value.

> **MUST NOT**: *  A cache recipient MUST NOT allow local time zones to influence the
      calculation or comparison of an age or expiration time.

> **SHOULD**: *  A cache recipient SHOULD consider a date with a zone abbreviation
      other than "GMT" to be invalid for calculating expiration.

   Note that freshness applies only to cache operation; it cannot be
   used to force a user agent to refresh its display or reload a
   resource.  See Section 6 for an explanation of the difference between
   caches and history mechanisms.

### 4.2.1  Calculating Freshness Lifetime

   A cache can calculate the freshness lifetime (denoted as
   freshness_lifetime) of a response by evaluating the following rules
   and using the first match:

   *  If the cache is shared and the s-maxage response directive
      (Section 5.2.2.10) is present, use its value, or

   *  If the max-age response directive (Section 5.2.2.1) is present,
      use its value, or

   *  If the Expires response header field (Section 5.3) is present, use
      its value minus the value of the Date response header field (using
      the time the message was received if it is not present, as per
      Section 6.6.1 of [HTTP]), or

   *  Otherwise, no explicit expiration time is present in the response.
      A heuristic freshness lifetime might be applicable; see
      Section 4.2.2.

   Note that this calculation is intended to reduce clock skew by using
   the clock information provided by the origin server whenever
   possible.

   When there is more than one value present for a given directive
   (e.g., two Expires header field lines or multiple Cache-Control: max-
   age directives), either the first occurrence should be used or the
   response should be considered stale.  If directives conflict (e.g.,
   both max-age and no-cache are present), the most restrictive
   directive should be honored.  Caches are encouraged to consider
   responses that have invalid freshness information (e.g., a max-age
   directive with non-integer content) to be stale.

### 4.2.2  Calculating Heuristic Freshness

   Since origin servers do not always provide explicit expiration times,
> **MAY**: a cache MAY assign a heuristic expiration time when an explicit time
   is not specified, employing algorithms that use other field values
   (such as the Last-Modified time) to estimate a plausible expiration
   time.  This specification does not provide specific algorithms, but
   it does impose worst-case constraints on their results.

> **MUST NOT**: A cache MUST NOT use heuristics to determine freshness when an
   explicit expiration time is present in the stored response.  Because
   of the requirements in Section 3, heuristics can only be used on
   responses without explicit freshness whose status codes are defined
   as "heuristically cacheable" (e.g., see Section 15.1 of [HTTP]) and
   on responses without explicit freshness that have been marked as
   explicitly cacheable (e.g., with a public response directive).

   Note that in previous specifications, heuristically cacheable
   response status codes were called "cacheable by default".

   If the response has a Last-Modified header field (Section 8.8.2 of
   [HTTP]), caches are encouraged to use a heuristic expiration value
   that is no more than some fraction of the interval since that time.
   A typical setting of this fraction might be 10%.

      |  *Note:* A previous version of the HTTP specification
      |  (Section 13.9 of [RFC2616]) prohibited caches from calculating
      |  heuristic freshness for URIs with query components (i.e., those
      |  containing "?").  In practice, this has not been widely
      |  implemented.  Therefore, origin servers are encouraged to send
      |  explicit directives (e.g., Cache-Control: no-cache) if they
      |  wish to prevent caching.

### 4.2.3  Calculating Age

   The Age header field is used to convey an estimated age of the
   response message when obtained from a cache.  The Age field value is
   the cache's estimate of the number of seconds since the origin server
   generated or validated the response.  The Age value is therefore the
   sum of the time that the response has been resident in each of the
   caches along the path from the origin server, plus the time it has
   been in transit along network paths.

   Age calculation uses the following data:

   "age_value"
      The term "age_value" denotes the value of the Age header field
      (Section 5.1), in a form appropriate for arithmetic operation; or
      0, if not available.

   "date_value"
      The term "date_value" denotes the value of the Date header field,
      in a form appropriate for arithmetic operations.  See
      Section 6.6.1 of [HTTP] for the definition of the Date header
      field and for requirements regarding responses without it.

   "now"
      The term "now" means the current value of this implementation's
      clock (Section 5.6.7 of [HTTP]).

   "request_time"
      The value of the clock at the time of the request that resulted in
      the stored response.

   "response_time"
      The value of the clock at the time the response was received.

   A response's age can be calculated in two entirely independent ways:

   1.  the "apparent_age": response_time minus date_value, if the
       implementation's clock is reasonably well synchronized to the
       origin server's clock.  If the result is negative, the result is
       replaced by zero.

   2.  the "corrected_age_value", if all of the caches along the
> **MUST**: response path implement HTTP/1.1 or greater.  A cache MUST
       interpret this value relative to the time the request was
       initiated, not the time that the response was received.

     apparent_age = max(0, response_time - date_value);

     response_delay = response_time - request_time;
     corrected_age_value = age_value + response_delay;

> **MAY**: The corrected_age_value MAY be used as the corrected_initial_age.  In
   circumstances where very old cache implementations that might not
   correctly insert Age are present, corrected_initial_age can be
   calculated more conservatively as

     corrected_initial_age = max(apparent_age, corrected_age_value);

   The current_age of a stored response can then be calculated by adding
   the time (in seconds) since the stored response was last validated by
   the origin server to the corrected_initial_age.

     resident_time = now - response_time;
     current_age = corrected_initial_age + resident_time;

### 4.2.4  Serving Stale Responses

   A "stale" response is one that either has explicit expiry information
   or is allowed to have heuristic expiry calculated, but is not fresh
   according to the calculations in Section 4.2.

> **MUST NOT**: A cache MUST NOT generate a stale response if it is prohibited by an
   explicit in-protocol directive (e.g., by a no-cache response
   directive, a must-revalidate response directive, or an applicable
   s-maxage or proxy-revalidate response directive; see Section 5.2.2).

> **MUST NOT**: A cache MUST NOT generate a stale response unless it is disconnected
   or doing so is explicitly permitted by the client or origin server
   (e.g., by the max-stale request directive in Section 5.2.1, extension
   directives such as those defined in [RFC5861], or configuration in
   accordance with an out-of-band contract).


---

## TurboHttp Compliance

**Status:** ❌ Missing

**Implementation Notes:**
TurboHttp does not perform freshness calculations. No age computation, freshness lifetime evaluation, or stale response serving logic exists. The client does not interpret `max-age`, `s-maxage`, `Expires`, or heuristic freshness rules.

**Key Gaps:**
- No age calculation algorithm (§4.2.3)
- No freshness lifetime computation from `max-age` or `Expires`
- No heuristic freshness estimation
- No stale response serving with `stale-while-revalidate` or `stale-if-error`
- No `min-fresh` or `max-stale` request directive handling
- No `Age` header generation

**Affected Components:** None

**Test References:** None

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
