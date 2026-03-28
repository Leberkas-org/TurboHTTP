---
title: "3.7.  Product Tokens"
rfc_number: 1945
rfc_section: "3.7"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 3.7: Product Tokens — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, product_tokens]
---

# 3.7.  Product Tokens

## 3.7  Product Tokens

   Product tokens are used to allow communicating applications to
   identify themselves via a simple product token, with an optional
   slash and version designator. Most fields using product tokens also
   allow subproducts which form a significant part of the application to



   be listed, separated by whitespace. By convention, the products are
   listed in order of their significance for identifying the
   application.


```abnf
       product         = token ["/" product-version]
       product-version = token
```


   Examples:

       User-Agent: CERN-LineMode/2.15 libwww/2.17b3

       Server: Apache/0.8.4

   Product tokens should be short and to the point -- use of them for
   advertizing or other non-essential information is explicitly
   forbidden. Although any token character may appear in a product-
   version, this token should only be used for a version identifier
   (i.e., successive versions of the same product should only differ in
   the product-version portion of the product value).

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
