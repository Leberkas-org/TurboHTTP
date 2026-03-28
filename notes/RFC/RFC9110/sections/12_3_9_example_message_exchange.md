---
title: "3.9.  Example Message Exchange"
rfc_number: 9110
rfc_section: "3.9"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.9: Example Message Exchange — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, example_message_exchange]
---

## 3.9.  Example Message Exchange

## 3.9  Example Message Exchange

   The following example illustrates a typical HTTP/1.1 message exchange
   for a GET request (Section 9.3.1) on the URI "http://www.example.com/
   hello.txt":

   Client request:

   GET /hello.txt HTTP/1.1
   User-Agent: curl/7.64.1
   Host: www.example.com
   Accept-Language: en, mi

   Server response:

   HTTP/1.1 200 OK
   Date: Mon, 27 Jul 2009 12:28:53 GMT
   Server: Apache
   Last-Modified: Wed, 22 Jul 2009 19:15:56 GMT
   ETag: "34aa387-d-1568eb00"
   Accept-Ranges: bytes
   Content-Length: 51
   Vary: Accept-Encoding
   Content-Type: text/plain

   Hello World! My content includes a trailing CRLF.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
