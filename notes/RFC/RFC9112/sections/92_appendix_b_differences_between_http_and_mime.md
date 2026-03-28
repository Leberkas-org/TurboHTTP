---
title: "Appendix B.  Differences between HTTP and MIME"
rfc_number: 9112
rfc_section: "Appendix B"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Appendix B: Differences between HTTP and MIME — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, differences_between_http_and_mime]
---

## Appendix B.  Differences between HTTP and MIME

Appendix B.  Differences between HTTP and MIME

   HTTP/1.1 uses many of the constructs defined for the Internet Message
   Format [RFC5322] and Multipurpose Internet Mail Extensions (MIME)
   [RFC2045] to allow a message body to be transmitted in an open
   variety of representations and with extensible fields.  However, some
   of these constructs have been reinterpreted to better fit the needs
   of interactive communication, leading to some differences in how MIME
   constructs are used within HTTP.  These differences were carefully
   chosen to optimize performance over binary connections, allow greater
   freedom in the use of new media types, ease date comparisons, and
   accommodate common implementations.

   This appendix describes specific areas where HTTP differs from MIME.
   Proxies and gateways to and from strict MIME environments need to be
   aware of these differences and provide the appropriate conversions
   where necessary.

B.1.  MIME-Version

   HTTP is not a MIME-compliant protocol.  However, messages can include
   a single MIME-Version header field to indicate what version of the
   MIME protocol was used to construct the message.  Use of the MIME-
   Version header field indicates that the message is in full
   conformance with the MIME protocol (as defined in [RFC2045]).
   Senders are responsible for ensuring full conformance (where
   possible) when exporting HTTP messages to strict MIME environments.

B.2.  Conversion to Canonical Form

   MIME requires that an Internet mail body part be converted to
   canonical form prior to being transferred, as described in Section 4
   of [RFC2049], and that content with a type of "text" represents line
   breaks as CRLF, forbidding the use of CR or LF outside of line break
   sequences [RFC2046].  In contrast, HTTP does not care whether CRLF,
   bare CR, or bare LF are used to indicate a line break within content.

   A proxy or gateway from HTTP to a strict MIME environment ought to
   translate all line breaks within text media types to the RFC 2049
   canonical form of CRLF.  Note, however, this might be complicated by
   the presence of a Content-Encoding and by the fact that HTTP allows
   the use of some charsets that do not use octets 13 and 10 to
   represent CR and LF, respectively.

   Conversion will break any cryptographic checksums applied to the
   original content unless the original content is already in canonical
   form.  Therefore, the canonical form is recommended for any content
   that uses such checksums in HTTP.

B.3.  Conversion of Date Formats

   HTTP/1.1 uses a restricted set of date formats (Section 5.6.7 of
   [HTTP]) to simplify the process of date comparison.  Proxies and
   gateways from other protocols ought to ensure that any Date header
   field present in a message conforms to one of the HTTP/1.1 formats
   and rewrite the date if necessary.

B.4.  Conversion of Content-Encoding

   MIME does not include any concept equivalent to HTTP's Content-
   Encoding header field.  Since this acts as a modifier on the media
   type, proxies and gateways from HTTP to MIME-compliant protocols
   ought to either change the value of the Content-Type header field or
   decode the representation before forwarding the message.  (Some
   experimental applications of Content-Type for Internet mail have used
   a media-type parameter of ";conversions=<content-coding>" to perform
   a function equivalent to Content-Encoding.  However, this parameter
   is not part of the MIME standards.)

B.5.  Conversion of Content-Transfer-Encoding

   HTTP does not use the Content-Transfer-Encoding field of MIME.
   Proxies and gateways from MIME-compliant protocols to HTTP need to
   remove any Content-Transfer-Encoding prior to delivering the response
   message to an HTTP client.

   Proxies and gateways from HTTP to MIME-compliant protocols are
   responsible for ensuring that the message is in the correct format
   and encoding for safe transport on that protocol, where "safe
   transport" is defined by the limitations of the protocol being used.
   Such a proxy or gateway ought to transform and label the data with an
   appropriate Content-Transfer-Encoding if doing so will improve the
   likelihood of safe transport over the destination protocol.

B.6.  MHTML and Line Length Limitations

   HTTP implementations that share code with MHTML [RFC2557]
   implementations need to be aware of MIME line length limitations.
   Since HTTP does not have this limitation, HTTP does not fold long
   lines.  MHTML messages being transported by HTTP follow all
   conventions of MHTML, including line length limitations and folding,
   canonicalization, etc., since HTTP transfers message-bodies without
   modification and, aside from the "multipart/byteranges" type
   (Section 14.6 of [HTTP]), does not interpret the content or any MIME
   header lines that might be contained therein.

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
