---
title: "3.4.  Character Sets"
rfc_number: 1945
rfc_section: "3.4"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 3.4: Character Sets — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, character_sets]
---

# 3.4.  Character Sets

## 3.4  Character Sets

   HTTP uses the same definition of the term "character set" as that
   described for MIME:

      The term "character set" is used in this document to refer to a
      method used with one or more tables to convert a sequence of
      octets into a sequence of characters. Note that unconditional
      conversion in the other direction is not required, in that not all
      characters may be available in a given character set and a
      character set may provide more than one sequence of octets to
      represent a particular character. This definition is intended to
      allow various kinds of character encodings, from simple single-
      table mappings such as US-ASCII to complex table switching methods
      such as those that use ISO 2022's techniques. However, the
      definition associated with a MIME character set name must fully
      specify the mapping to be performed from octets to characters. In
      particular, use of external profiling information to determine the
      exact mapping is not permitted.

      Note: This use of the term "character set" is more commonly
      referred to as a "character encoding." However, since HTTP and
      MIME share the same registry, it is important that the terminology
      also be shared.

   HTTP character sets are identified by case-insensitive tokens. The
   complete set of tokens are defined by the IANA Character Set registry
   [15]. However, because that registry does not define a single,
   consistent token for each character set, we define here the preferred
   names for those character sets most likely to be used with HTTP
   entities. These character sets include those registered by RFC 1521
   [5] -- the US-ASCII [17] and ISO-8859 [18] character sets -- and
   other names specifically recommended for use within MIME charset
   parameters.


```abnf
     charset = "US-ASCII"
             | "ISO-8859-1" | "ISO-8859-2" | "ISO-8859-3"
             | "ISO-8859-4" | "ISO-8859-5" | "ISO-8859-6"
             | "ISO-8859-7" | "ISO-8859-8" | "ISO-8859-9"
             | "ISO-2022-JP" | "ISO-2022-JP-2" | "ISO-2022-KR"
             | "UNICODE-1-1" | "UNICODE-1-1-UTF-7" | "UNICODE-1-1-UTF-8"
             | token
```


   Although HTTP allows an arbitrary token to be used as a charset
   value, any token that has a predefined value within the IANA
   Character Set registry [15] must represent the character set defined



   by that registry. Applications should limit their use of character
   sets to those defined by the IANA registry.

   The character set of an entity body should be labelled as the lowest
   common denominator of the character codes used within that body, with
   the exception that no label is preferred over the labels US-ASCII or
   ISO-8859-1.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
