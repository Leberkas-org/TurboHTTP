---
title: "11.  IANA Considerations"
rfc_number: 9113
rfc_section: "11"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 11: IANA Considerations — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, iana_considerations]
---

## 11.  IANA Considerations

11.  IANA Considerations

   This revision of HTTP/2 marks the HTTP2-Settings header field and the
   h2c upgrade token, both defined in [RFC7540], as obsolete.

   Section 11 of [RFC7540] registered the h2 and h2c ALPN identifiers
   along with the PRI HTTP method.  RFC 7540 also established a registry
   for frame types, settings, and error codes.  These registrations and
   registries apply to HTTP/2, but are not redefined in this document.

   IANA has updated references to RFC 7540 in the following registries
   to refer to this document: "TLS Application-Layer Protocol
   Negotiation (ALPN) Protocol IDs", "HTTP/2 Frame Type", "HTTP/2
   Settings", "HTTP/2 Error Code", and "HTTP Method Registry".  The
   registration of the PRI method has been updated to refer to
   Section 3.4; all other section numbers have not changed.

   IANA has changed the policy on those portions of the "HTTP/2 Frame
   Type" and "HTTP/2 Settings" registries that were reserved for
   Experimental Use in RFC 7540.  These portions of the registries shall
   operate on the same policy as the remainder of each registry.

## 11.1  HTTP2-Settings Header Field Registration

   This section marks the HTTP2-Settings header field registered by
   Section 11.5 of [RFC7540] in the "Hypertext Transfer Protocol (HTTP)
   Field Name Registry" as obsolete.  This capability has been removed:
   see Section 3.1.  The registration is updated to include the details
   as required by Section 18.4 of [HTTP]:

   Field Name:  HTTP2-Settings

   Status:  obsoleted

   Reference:  Section 3.2.1 of [RFC7540]

   Comments:  Obsolete; see Section 11.1 of this document.

## 11.2  The h2c Upgrade Token

   This section records the h2c upgrade token registered by Section 11.8
   of [RFC7540] in the "Hypertext Transfer Protocol (HTTP) Upgrade Token
   Registry" as obsolete.  This capability has been removed: see
   Section 3.1.  The registration is updated as follows:

   Value:  h2c

   Description:  (OBSOLETE) Hypertext Transfer Protocol version 2
      (HTTP/2)

   Expected Version Tokens:  None

   Reference:  Section 3.1 of this document

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
