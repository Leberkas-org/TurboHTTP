---
title: "2.  Notational Conventions and Generic Grammar"
rfc_number: 1945
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 2: Notational Conventions and Generic Grammar — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, notational_conventions_and_generic_grammar]
---

# 2.  Notational Conventions and Generic Grammar


## 2.1  Augmented BNF

   All of the mechanisms specified in this document are described in
   both prose and an augmented Backus-Naur Form (BNF) similar to that
   used by RFC 822 [7]. Implementors will need to be familiar with the
   notation in order to understand this specification. The augmented BNF
   includes the following constructs:





```abnf
   name = definition
```


       The name of a rule is simply the name itself (without any
       enclosing "<" and ">") and is separated from its definition by
       the equal character "=". Whitespace is only significant in that
       indentation of continuation lines is used to indicate a rule
       definition that spans more than one line. Certain basic rules
       are in uppercase, such as SP, LWS, HT, CRLF, DIGIT, ALPHA, etc.
       Angle brackets are used within definitions whenever their
       presence will facilitate discerning the use of rule names.

   "literal"

       Quotation marks surround literal text. Unless stated otherwise,
       the text is case-insensitive.

   rule1 | rule2

       Elements separated by a bar ("I") are alternatives,
       e.g., "yes | no" will accept yes or no.

   (rule1 rule2)

       Elements enclosed in parentheses are treated as a single
       element. Thus, "(elem (foo | bar) elem)" allows the token
       sequences "elem foo elem" and "elem bar elem".

   *rule

       The character "*" preceding an element indicates repetition. The
       full form is "<n>*<m>element" indicating at least <n> and at
       most <m> occurrences of element. Default values are 0 and
       infinity so that "*(element)" allows any number, including zero;
       "1*element" requires at least one; and "1*2element" allows one
       or two.

   [rule]

       Square brackets enclose optional elements; "[foo bar]" is
       equivalent to "*1(foo bar)".

   N rule

       Specific repetition: "<n>(element)" is equivalent to
       "<n>*<n>(element)"; that is, exactly <n> occurrences of
       (element). Thus 2DIGIT is a 2-digit number, and 3ALPHA is a
       string of three alphabetic characters.




   #rule

       A construct "#" is defined, similar to "*", for defining lists
       of elements. The full form is "<n>#<m>element" indicating at
       least <n> and at most <m> elements, each separated by one or
       more commas (",") and optional linear whitespace (LWS). This
       makes the usual form of lists very easy; a rule such as
       "( *LWS element *( *LWS "," *LWS element ))" can be shown as
       "1#element". Wherever this construct is used, null elements are
       allowed, but do not contribute to the count of elements present.
       That is, "(element), , (element)" is permitted, but counts as
       only two elements. Therefore, where at least one element is
       required, at least one non-null element must be present. Default
       values are 0 and infinity so that "#(element)" allows any
       number, including zero; "1#element" requires at least one; and
       "1#2element" allows one or two.

   ; comment

       A semi-colon, set off some distance to the right of rule text,
       starts a comment that continues to the end of line. This is a
       simple way of including useful notes in parallel with the
       specifications.

   implied *LWS

       The grammar described by this specification is word-based.
       Except where noted otherwise, linear whitespace (LWS) can be
       included between any two adjacent words (token or
       quoted-string), and between adjacent tokens and delimiters
       (tspecials), without changing the interpretation of a field. At
       least one delimiter (tspecials) must exist between any two
       tokens, since they would otherwise be interpreted as a single
       token. However, applications should attempt to follow "common
       form" when generating HTTP constructs, since there exist some
       implementations that fail to accept anything beyond the common
       forms.

## 2.2  Basic Rules

   The following rules are used throughout this specification to
   describe basic parsing constructs. The US-ASCII coded character set
   is defined by [17].


```abnf
       OCTET          = <any 8-bit sequence of data>
       CHAR           = <any US-ASCII character (octets 0 - 127)>
       UPALPHA        = <any US-ASCII uppercase letter "A".."Z">
       LOALPHA        = <any US-ASCII lowercase letter "a".."z">
```





```abnf
       ALPHA          = UPALPHA | LOALPHA
       DIGIT          = <any US-ASCII digit "0".."9">
       CTL            = <any US-ASCII control character
                        (octets 0 - 31) and DEL (127)>
       CR             = <US-ASCII CR, carriage return (13)>
       LF             = <US-ASCII LF, linefeed (10)>
       SP             = <US-ASCII SP, space (32)>
       HT             = <US-ASCII HT, horizontal-tab (9)>
       <">            = <US-ASCII double-quote mark (34)>
```


   HTTP/1.0 defines the octet sequence CR LF as the end-of-line marker
   for all protocol elements except the Entity-Body (see Appendix B for
   tolerant applications). The end-of-line marker within an Entity-Body
   is defined by its associated media type, as described in Section 3.6.


```abnf
       CRLF           = CR LF
```


   HTTP/1.0 headers may be folded onto multiple lines if each
   continuation line begins with a space or horizontal tab. All linear
   whitespace, including folding, has the same semantics as SP.


```abnf
       LWS            = [CRLF] 1*( SP | HT )
```


   However, folding of header lines is not expected by some
   applications, and should not be generated by HTTP/1.0 applications.

   The TEXT rule is only used for descriptive field contents and values
   that are not intended to be interpreted by the message parser. Words
   of *TEXT may contain octets from character sets other than US-ASCII.


```abnf
       TEXT           = <any OCTET except CTLs,
                        but including LWS>
```


   Recipients of header field TEXT containing octets outside the US-
   ASCII character set may assume that they represent ISO-8859-1
   characters.

   Hexadecimal numeric characters are used in several protocol elements.


```abnf
       HEX            = "A" | "B" | "C" | "D" | "E" | "F"
                      | "a" | "b" | "c" | "d" | "e" | "f" | DIGIT
```


   Many HTTP/1.0 header field values consist of words separated by LWS
   or special characters. These special characters must be in a quoted
   string to be used within a parameter value.


```abnf
       word           = token | quoted-string
```






```abnf
       token          = 1*<any CHAR except CTLs or tspecials>

       tspecials      = "(" | ")" | "<" | ">" | "@"
                      | "," | ";" | ":" | "\" | <">
                      | "/" | "[" | "]" | "?" | "="
                      | "{" | "}" | SP | HT
```


   Comments may be included in some HTTP header fields by surrounding
   the comment text with parentheses. Comments are only allowed in
   fields containing "comment" as part of their field value definition.
   In all other fields, parentheses are considered part of the field
   value.


```abnf
       comment        = "(" *( ctext | comment ) ")"
       ctext          = <any TEXT excluding "(" and ")">
```


   A string of text is parsed as a single word if it is quoted using
   double-quote marks.


```abnf
       quoted-string  = ( <"> *(qdtext) <"> )

       qdtext         = <any CHAR except <"> and CTLs,
                        but including LWS>
```


   Single-character quoting using the backslash ("\") character is not
   permitted in HTTP/1.0.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
