---
title: "8.5.  Content-Language"
rfc_number: 9110
rfc_section: "8.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 8.5: Content-Language — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, content-language]
---

## 8.5.  Content-Language

## 8.5  Content-Language

   The "Content-Language" header field describes the natural language(s)
   of the intended audience for the representation.  Note that this
   might not be equivalent to all the languages used within the
   representation.


```abnf
     Content-Language = #language-tag
```


   Language tags are defined in Section 8.5.1.  The primary purpose of
   Content-Language is to allow a user to identify and differentiate
   representations according to the users' own preferred language.
   Thus, if the content is intended only for a Danish-literate audience,
   the appropriate field is

   Content-Language: da

   If no Content-Language is specified, the default is that the content
   is intended for all language audiences.  This might mean that the
   sender does not consider it to be specific to any natural language,
   or that the sender does not know for which language it is intended.

> **MAY**: Multiple languages MAY be listed for content that is intended for
   multiple audiences.  For example, a rendition of the "Treaty of
   Waitangi", presented simultaneously in the original Maori and English
   versions, would call for

   Content-Language: mi, en

   However, just because multiple languages are present within a
   representation does not mean that it is intended for multiple
   linguistic audiences.  An example would be a beginner's language
   primer, such as "A First Lesson in Latin", which is clearly intended
   to be used by an English-literate audience.  In this case, the
   Content-Language would properly only include "en".

> **MAY**: Content-Language MAY be applied to any media type -- it is not
   limited to textual documents.

### 8.5.1  Language Tags

   A language tag, as defined in [RFC5646], identifies a natural
   language spoken, written, or otherwise conveyed by human beings for
   communication of information to other human beings.  Computer
   languages are explicitly excluded.

   HTTP uses language tags within the Accept-Language and
   Content-Language header fields.  Accept-Language uses the broader
   language-range production defined in Section 12.5.4, whereas
   Content-Language uses the language-tag production defined below.


```abnf
     language-tag = <Language-Tag, see [RFC5646], Section 2.1>
```


   A language tag is a sequence of one or more case-insensitive subtags,
   each separated by a hyphen character ("-", %x2D).  In most cases, a
   language tag consists of a primary language subtag that identifies a
   broad family of related languages (e.g., "en" = English), which is
   optionally followed by a series of subtags that refine or narrow that
   language's range (e.g., "en-CA" = the variety of English as
   communicated in Canada).  Whitespace is not allowed within a language
   tag.  Example tags include:

     fr, en-US, es-419, az-Arab, x-pig-latin, man-Nkoo-GN

   See [RFC5646] for further information.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
