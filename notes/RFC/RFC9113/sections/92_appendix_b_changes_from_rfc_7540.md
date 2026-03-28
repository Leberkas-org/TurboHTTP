---
title: "Appendix B.  Changes from RFC 7540"
rfc_number: 9113
rfc_section: "Appendix B"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Appendix B: Changes from RFC 7540 — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, changes_from_rfc_7540]
---

## Appendix B.  Changes from RFC 7540

Appendix B.  Changes from RFC 7540

   This revision includes the following substantive changes:

   *  Use of TLS 1.3 was defined based on [RFC8740], which this document
      obsoletes.

   *  The priority scheme defined in RFC 7540 is deprecated.
      Definitions for the format of the PRIORITY frame and the priority
      fields in the HEADERS frame have been retained, plus the rules
      governing when PRIORITY frames can be sent and received, but the
      semantics of these fields are only described in RFC 7540.  The
      priority signaling scheme from RFC 7540 was not successful.  Using
      the simpler signaling in [HTTP-PRIORITY] is recommended.

   *  The HTTP/1.1 Upgrade mechanism is deprecated and no longer
      specified in this document.  It was never widely deployed, with
      plaintext HTTP/2 users choosing to use the prior-knowledge
      implementation instead.

   *  Validation for field names and values has been narrowed.  The
      validation that is mandatory for intermediaries is precisely
      defined, and error reporting for requests has been amended to
      encourage sending 400-series status codes.

   *  The ranges of codepoints for settings and frame types that were
      reserved for Experimental Use are now available for general use.

   *  Connection-specific header fields -- which are prohibited -- are
      more precisely and comprehensively identified.

   *  Host and ":authority" are no longer permitted to disagree.

   *  Rules for sending Dynamic Table Size Update instructions after
      changes in settings have been clarified in Section 4.3.1.

   Editorial changes are also included.  In particular, changes to
   terminology and document structure are in response to updates to core
   HTTP semantics [HTTP].  Those documents now include some concepts
   that were first defined in RFC 7540, such as the 421 status code or
   connection coalescing.

Acknowledgments

   Credit for non-trivial input to this document is owed to a large
   number of people who have contributed to the HTTP Working Group over
   the years.  [RFC7540] contains a more extensive list of people that
   deserve acknowledgment for their contributions.

Contributors

   Mike Belshe and Roberto Peon authored the text that this document is
   based on.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
