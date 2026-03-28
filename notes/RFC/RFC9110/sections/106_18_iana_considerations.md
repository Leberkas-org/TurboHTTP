---
title: "18.  IANA Considerations"
rfc_number: 9110
rfc_section: "18"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 18: IANA Considerations — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, iana_considerations]
---

## 18.  IANA Considerations

18.  IANA Considerations

   The change controller for the following registrations is: "IETF
   (iesg@ietf.org) - Internet Engineering Task Force".

## 18.1  URI Scheme Registration

   IANA has updated the "Uniform Resource Identifier (URI) Schemes"
   registry [BCP35] at <https://www.iana.org/assignments/uri-schemes/>
   with the permanent schemes listed in Table 2 in Section 4.2.

## 18.2  Method Registration

   IANA has updated the "Hypertext Transfer Protocol (HTTP) Method
   Registry" at <https://www.iana.org/assignments/http-methods> with the
   registration procedure of Section 16.1.1 and the method names
   summarized in the following table.

                 +=========+======+============+=========+
                 | Method  | Safe | Idempotent | Section |
                 +=========+======+============+=========+
                 | CONNECT | no   | no         | 9.3.6   |
                 +---------+------+------------+---------+
                 | DELETE  | no   | yes        | 9.3.5   |
                 +---------+------+------------+---------+
                 | GET     | yes  | yes        | 9.3.1   |
                 +---------+------+------------+---------+
                 | HEAD    | yes  | yes        | 9.3.2   |
                 +---------+------+------------+---------+
                 | OPTIONS | yes  | yes        | 9.3.7   |
                 +---------+------+------------+---------+
                 | POST    | no   | no         | 9.3.3   |
                 +---------+------+------------+---------+
                 | PUT     | no   | yes        | 9.3.4   |
                 +---------+------+------------+---------+
                 | TRACE   | yes  | yes        | 9.3.8   |
                 +---------+------+------------+---------+
                 | *       | no   | no         | 18.2    |
                 +---------+------+------------+---------+

                                  Table 7

   The method name "*" is reserved because using "*" as a method name
   would conflict with its usage as a wildcard in some fields (e.g.,
   "Access-Control-Request-Method").

## 18.3  Status Code Registration

   IANA has updated the "Hypertext Transfer Protocol (HTTP) Status Code
   Registry" at <https://www.iana.org/assignments/http-status-codes>
   with the registration procedure of Section 16.2.1 and the status code
   values summarized in the following table.

            +=======+===============================+=========+
            | Value | Description                   | Section |
            +=======+===============================+=========+
            | 100   | Continue                      | 15.2.1  |
            +-------+-------------------------------+---------+
            | 101   | Switching Protocols           | 15.2.2  |
            +-------+-------------------------------+---------+
            | 200   | OK                            | 15.3.1  |
            +-------+-------------------------------+---------+
            | 201   | Created                       | 15.3.2  |
            +-------+-------------------------------+---------+
            | 202   | Accepted                      | 15.3.3  |
            +-------+-------------------------------+---------+
            | 203   | Non-Authoritative Information | 15.3.4  |
            +-------+-------------------------------+---------+
            | 204   | No Content                    | 15.3.5  |
            +-------+-------------------------------+---------+
            | 205   | Reset Content                 | 15.3.6  |
            +-------+-------------------------------+---------+
            | 206   | Partial Content               | 15.3.7  |
            +-------+-------------------------------+---------+
            | 300   | Multiple Choices              | 15.4.1  |
            +-------+-------------------------------+---------+
            | 301   | Moved Permanently             | 15.4.2  |
            +-------+-------------------------------+---------+
            | 302   | Found                         | 15.4.3  |
            +-------+-------------------------------+---------+
            | 303   | See Other                     | 15.4.4  |
            +-------+-------------------------------+---------+
            | 304   | Not Modified                  | 15.4.5  |
            +-------+-------------------------------+---------+
            | 305   | Use Proxy                     | 15.4.6  |
            +-------+-------------------------------+---------+
            | 306   | (Unused)                      | 15.4.7  |
            +-------+-------------------------------+---------+
            | 307   | Temporary Redirect            | 15.4.8  |
            +-------+-------------------------------+---------+
            | 308   | Permanent Redirect            | 15.4.9  |
            +-------+-------------------------------+---------+
            | 400   | Bad Request                   | 15.5.1  |
            +-------+-------------------------------+---------+
            | 401   | Unauthorized                  | 15.5.2  |
            +-------+-------------------------------+---------+
            | 402   | Payment Required              | 15.5.3  |
            +-------+-------------------------------+---------+
            | 403   | Forbidden                     | 15.5.4  |
            +-------+-------------------------------+---------+
            | 404   | Not Found                     | 15.5.5  |
            +-------+-------------------------------+---------+
            | 405   | Method Not Allowed            | 15.5.6  |
            +-------+-------------------------------+---------+
            | 406   | Not Acceptable                | 15.5.7  |
            +-------+-------------------------------+---------+
            | 407   | Proxy Authentication Required | 15.5.8  |
            +-------+-------------------------------+---------+
            | 408   | Request Timeout               | 15.5.9  |
            +-------+-------------------------------+---------+
            | 409   | Conflict                      | 15.5.10 |
            +-------+-------------------------------+---------+
            | 410   | Gone                          | 15.5.11 |
            +-------+-------------------------------+---------+
            | 411   | Length Required               | 15.5.12 |
            +-------+-------------------------------+---------+
            | 412   | Precondition Failed           | 15.5.13 |
            +-------+-------------------------------+---------+
            | 413   | Content Too Large             | 15.5.14 |
            +-------+-------------------------------+---------+
            | 414   | URI Too Long                  | 15.5.15 |
            +-------+-------------------------------+---------+
            | 415   | Unsupported Media Type        | 15.5.16 |
            +-------+-------------------------------+---------+
            | 416   | Range Not Satisfiable         | 15.5.17 |
            +-------+-------------------------------+---------+
            | 417   | Expectation Failed            | 15.5.18 |
            +-------+-------------------------------+---------+
            | 418   | (Unused)                      | 15.5.19 |
            +-------+-------------------------------+---------+
            | 421   | Misdirected Request           | 15.5.20 |
            +-------+-------------------------------+---------+
            | 422   | Unprocessable Content         | 15.5.21 |
            +-------+-------------------------------+---------+
            | 426   | Upgrade Required              | 15.5.22 |
            +-------+-------------------------------+---------+
            | 500   | Internal Server Error         | 15.6.1  |
            +-------+-------------------------------+---------+
            | 501   | Not Implemented               | 15.6.2  |
            +-------+-------------------------------+---------+
            | 502   | Bad Gateway                   | 15.6.3  |
            +-------+-------------------------------+---------+
            | 503   | Service Unavailable           | 15.6.4  |
            +-------+-------------------------------+---------+
            | 504   | Gateway Timeout               | 15.6.5  |
            +-------+-------------------------------+---------+
            | 505   | HTTP Version Not Supported    | 15.6.6  |
            +-------+-------------------------------+---------+

                                  Table 8

## 18.4  Field Name Registration

   This specification updates the HTTP-related aspects of the existing
   registration procedures for message header fields defined in
   [RFC3864].  It replaces the old procedures as they relate to HTTP by
   defining a new registration procedure and moving HTTP field
   definitions into a separate registry.

   IANA has created a new registry titled "Hypertext Transfer Protocol
   (HTTP) Field Name Registry" as outlined in Section 16.3.1.

   IANA has moved all entries in the "Permanent Message Header Field
   Names" and "Provisional Message Header Field Names" registries (see
   <https://www.iana.org/assignments/message-headers/>) with the
   protocol 'http' to this registry and has applied the following
   changes:

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
