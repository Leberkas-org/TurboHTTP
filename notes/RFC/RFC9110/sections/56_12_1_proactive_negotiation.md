---
title: "12.1.  Proactive Negotiation"
rfc_number: 9110
rfc_section: "12.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 12.1: Proactive Negotiation — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, proactive_negotiation]
---

## 12.1.  Proactive Negotiation

12.  Content Negotiation

   When responses convey content, whether indicating a success or an
   error, the origin server often has different ways of representing
   that information; for example, in different formats, languages, or
   encodings.  Likewise, different users or user agents might have
   differing capabilities, characteristics, or preferences that could
   influence which representation, among those available, would be best
   to deliver.  For this reason, HTTP provides mechanisms for content
   negotiation.

   This specification defines three patterns of content negotiation that
   can be made visible within the protocol: "proactive" negotiation,
   where the server selects the representation based upon the user
   agent's stated preferences; "reactive" negotiation, where the server
   provides a list of representations for the user agent to choose from;
   and "request content" negotiation, where the user agent selects the
   representation for a future request based upon the server's stated
   preferences in past responses.

   Other patterns of content negotiation include "conditional content",
   where the representation consists of multiple parts that are
   selectively rendered based on user agent parameters, "active
   content", where the representation contains a script that makes
   additional (more specific) requests based on the user agent
   characteristics, and "Transparent Content Negotiation" ([RFC2295]),
   where content selection is performed by an intermediary.  These
   patterns are not mutually exclusive, and each has trade-offs in
   applicability and practicality.

   Note that, in all cases, HTTP is not aware of the resource semantics.
   The consistency with which an origin server responds to requests,
   over time and over the varying dimensions of content negotiation, and
   thus the "sameness" of a resource's observed representations over
   time, is determined entirely by whatever entity or algorithm selects
   or generates those responses.

## 12.1  Proactive Negotiation

   When content negotiation preferences are sent by the user agent in a
   request to encourage an algorithm located at the server to select the
   preferred representation, it is called "proactive negotiation"
   (a.k.a., "server-driven negotiation").  Selection is based on the
   available representations for a response (the dimensions over which
   it might vary, such as language, content coding, etc.) compared to
   various information supplied in the request, including both the
   explicit negotiation header fields below and implicit
   characteristics, such as the client's network address or parts of the
   User-Agent field.

   Proactive negotiation is advantageous when the algorithm for
   selecting from among the available representations is difficult to
   describe to a user agent, or when the server desires to send its
   "best guess" to the user agent along with the first response (when
   that "best guess" is good enough for the user, this avoids the round-
   trip delay of a subsequent request).  In order to improve the
> **MAY**: server's guess, a user agent MAY send request header fields that
   describe its preferences.

   Proactive negotiation has serious disadvantages:

   *  It is impossible for the server to accurately determine what might
      be "best" for any given user, since that would require complete
      knowledge of both the capabilities of the user agent and the
      intended use for the response (e.g., does the user want to view it
      on screen or print it on paper?);

   *  Having the user agent describe its capabilities in every request
      can be both very inefficient (given that only a small percentage
      of responses have multiple representations) and a potential risk
      to the user's privacy;

   *  It complicates the implementation of an origin server and the
      algorithms for generating responses to a request; and,

   *  It limits the reusability of responses for shared caching.

   A user agent cannot rely on proactive negotiation preferences being
   consistently honored, since the origin server might not implement
   proactive negotiation for the requested resource or might decide that
   sending a response that doesn't conform to the user agent's
   preferences is better than sending a 406 (Not Acceptable) response.

   A Vary header field (Section 12.5.5) is often sent in a response
   subject to proactive negotiation to indicate what parts of the
   request information were used in the selection algorithm.

   The request header fields Accept, Accept-Charset, Accept-Encoding,
   and Accept-Language are defined below for a user agent to engage in
   proactive negotiation of the response content.  The preferences sent
   in these fields apply to any content in the response, including
   representations of the target resource, representations of error or
   processing status, and potentially even the miscellaneous text
   strings that might appear within the protocol.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
