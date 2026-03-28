---
title: "11.  Security Considerations"
rfc_number: 9112
rfc_section: "11"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 11: Security Considerations — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, security_considerations]
---

## 11.  Security Considerations

11.  Security Considerations

   This section is meant to inform developers, information providers,
   and users about known security considerations relevant to HTTP
   message syntax and parsing.  Security considerations about HTTP
   semantics, content, and routing are addressed in [HTTP].

## 11.1  Response Splitting

   Response splitting (a.k.a. CRLF injection) is a common technique,
   used in various attacks on Web usage, that exploits the line-based
   nature of HTTP message framing and the ordered association of
   requests to responses on persistent connections [Klein].  This
   technique can be particularly damaging when the requests pass through
   a shared cache.

   Response splitting exploits a vulnerability in servers (usually
   within an application server) where an attacker can send encoded data
   within some parameter of the request that is later decoded and echoed
   within any of the response header fields of the response.  If the
   decoded data is crafted to look like the response has ended and a
   subsequent response has begun, the response has been split, and the
   content within the apparent second response is controlled by the
   attacker.  The attacker can then make any other request on the same
   persistent connection and trick the recipients (including
   intermediaries) into believing that the second half of the split is
   an authoritative answer to the second request.

   For example, a parameter within the request-target might be read by
   an application server and reused within a redirect, resulting in the
   same parameter being echoed in the Location header field of the
   response.  If the parameter is decoded by the application and not
   properly encoded when placed in the response field, the attacker can
   send encoded CRLF octets and other content that will make the
   application's single response look like two or more responses.

   A common defense against response splitting is to filter requests for
   data that looks like encoded CR and LF (e.g., "%0D" and "%0A").
   However, that assumes the application server is only performing URI
   decoding rather than more obscure data transformations like charset
   transcoding, XML entity translation, base64 decoding, sprintf
   reformatting, etc.  A more effective mitigation is to prevent
   anything other than the server's core protocol libraries from sending
   a CR or LF within the header section, which means restricting the
   output of header fields to APIs that filter for bad octets and not
   allowing application servers to write directly to the protocol
   stream.

## 11.2  Request Smuggling

   Request smuggling ([Linhart]) is a technique that exploits
   differences in protocol parsing among various recipients to hide
   additional requests (which might otherwise be blocked or disabled by
   policy) within an apparently harmless request.  Like response
   splitting, request smuggling can lead to a variety of attacks on HTTP
   usage.

   This specification has introduced new requirements on request
   parsing, particularly with regard to message framing in Section 6.3,
   to reduce the effectiveness of request smuggling.

## 11.3  Message Integrity

   HTTP does not define a specific mechanism for ensuring message
   integrity, instead relying on the error-detection ability of
   underlying transport protocols and the use of length or chunk-
   delimited framing to detect completeness.  Historically, the lack of
   a single integrity mechanism has been justified by the informal
   nature of most HTTP communication.  However, the prevalence of HTTP
   as an information access mechanism has resulted in its increasing use
   within environments where verification of message integrity is
   crucial.

   The mechanisms provided with the "https" scheme, such as
   authenticated encryption, provide protection against modification of
   messages.  Care is needed, however, to ensure that connection closure
   cannot be used to truncate messages (see Section 9.8).  User agents
   might refuse to accept incomplete messages or treat them specially.
   For example, a browser being used to view medical history or drug
   interaction information needs to indicate to the user when such
   information is detected by the protocol to be incomplete, expired, or
   corrupted during transfer.  Such mechanisms might be selectively
   enabled via user agent extensions or the presence of message
   integrity metadata in a response.

   The "http" scheme provides no protection against accidental or
   malicious modification of messages.

   Extensions to the protocol might be used to mitigate the risk of
   unwanted modification of messages by intermediaries, even when the
   "https" scheme is used.  Integrity might be assured by using message
   authentication codes or digital signatures that are selectively added
   to messages via extensible metadata fields.

## 11.4  Message Confidentiality

   HTTP relies on underlying transport protocols to provide message
   confidentiality when that is desired.  HTTP has been specifically
   designed to be independent of the transport protocol, such that it
   can be used over many forms of encrypted connection, with the
   selection of such transports being identified by the choice of URI
   scheme or within user agent configuration.

   The "https" scheme can be used to identify resources that require a
   confidential connection, as described in Section 4.2.2 of [HTTP].

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
