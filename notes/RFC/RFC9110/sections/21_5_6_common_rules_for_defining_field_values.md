---
title: "5.6.  Common Rules for Defining Field Values"
rfc_number: 9110
rfc_section: "5.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 5.6: Common Rules for Defining Field Values — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, common_rules_for_defining_field_values]
---

## 5.6.  Common Rules for Defining Field Values

## 5.6  Common Rules for Defining Field Values

5.6.1.  Lists (#rule ABNF Extension)

   A #rule extension to the ABNF rules of [RFC5234] is used to improve
   readability in the definitions of some list-based field values.

   A construct "#" is defined, similar to "*", for defining comma-
   delimited lists of elements.  The full form is "<n>#<m>element"
   indicating at least <n> and at most <m> elements, each separated by a
   single comma (",") and optional whitespace (OWS, defined in
   Section 5.6.3).

#### 5.6.1.1  Sender Requirements

> **MUST NOT**: In any production that uses the list construct, a sender MUST NOT
   generate empty list elements.  In other words, a sender has to
   generate lists that satisfy the following syntax:

     1#element => element *( OWS "," OWS element )

   and:

     #element => [ 1#element ]

   and for n >= 1 and m > 1:

     <n>#<m>element => element <n-1>*<m-1>( OWS "," OWS element )

   Appendix A shows the collected ABNF for senders after the list
   constructs have been expanded.

#### 5.6.1.2  Recipient Requirements

   Empty elements do not contribute to the count of elements present.  A
> **MUST**: recipient MUST parse and ignore a reasonable number of empty list
   elements: enough to handle common mistakes by senders that merge
   values, but not so much that they could be used as a denial-of-
> **MUST**: service mechanism.  In other words, a recipient MUST accept lists
   that satisfy the following syntax:

     #element => [ element ] *( OWS "," OWS [ element ] )

   Note that because of the potential presence of empty list elements,
   the RFC 5234 ABNF cannot enforce the cardinality of list elements,
   and consequently all cases are mapped as if there was no cardinality
   specified.

   For example, given these ABNF productions:


```abnf
     example-list      = 1#example-list-elmt
     example-list-elmt = token ; see Section 5.6.2
```


   Then the following are valid values for example-list (not including
   the double quotes, which are present for delimitation only):

     "foo,bar"
     "foo ,bar,"
     "foo , ,bar,charlie"

   In contrast, the following values would be invalid, since at least
   one non-empty element is required by the example-list production:

     ""
     ","
     ",   ,"

### 5.6.2  Tokens

   Tokens are short textual identifiers that do not include whitespace
   or delimiters.


```abnf
     token          = 1*tchar

     tchar          = "!" / "#" / "$" / "%" / "&" / "'" / "*"
                    / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~"
                    / DIGIT / ALPHA
                    ; any VCHAR, except delimiters
```


   Many HTTP field values are defined using common syntax components,
   separated by whitespace or specific delimiting characters.
   Delimiters are chosen from the set of US-ASCII visual characters not
   allowed in a token (DQUOTE and "(),/:;<=>?@[\]{}").

### 5.6.3  Whitespace

   This specification uses three rules to denote the use of linear
   whitespace: OWS (optional whitespace), RWS (required whitespace), and
   BWS ("bad" whitespace).

   The OWS rule is used where zero or more linear whitespace octets
   might appear.  For protocol elements where optional whitespace is
> **SHOULD**: preferred to improve readability, a sender SHOULD generate the
   optional whitespace as a single SP; otherwise, a sender SHOULD NOT
   generate optional whitespace except as needed to overwrite invalid or
   unwanted protocol elements during in-place message filtering.

   The RWS rule is used when at least one linear whitespace octet is
> **SHOULD**: required to separate field tokens.  A sender SHOULD generate RWS as a
   single SP.

   OWS and RWS have the same semantics as a single SP.  Any content
> **MAY**: known to be defined as OWS or RWS MAY be replaced with a single SP
   before interpreting it or forwarding the message downstream.

   The BWS rule is used where the grammar allows optional whitespace
> **MUST NOT**: only for historical reasons.  A sender MUST NOT generate BWS in
   messages.  A recipient MUST parse for such bad whitespace and remove
   it before interpreting the protocol element.

> **MAY**: BWS has no semantics.  Any content known to be defined as BWS MAY be
   removed before interpreting it or forwarding the message downstream.


```abnf
     OWS            = *( SP / HTAB )
                    ; optional whitespace
     RWS            = 1*( SP / HTAB )
                    ; required whitespace
     BWS            = OWS
                    ; "bad" whitespace
```


### 5.6.4  Quoted Strings

   A string of text is parsed as a single value if it is quoted using
   double-quote marks.


```abnf
     quoted-string  = DQUOTE *( qdtext / quoted-pair ) DQUOTE
     qdtext         = HTAB / SP / %x21 / %x23-5B / %x5D-7E / obs-text
```


   The backslash octet ("\") can be used as a single-octet quoting
   mechanism within quoted-string and comment constructs.  Recipients
> **MUST**: that process the value of a quoted-string MUST handle a quoted-pair
   as if it were replaced by the octet following the backslash.


```abnf
     quoted-pair    = "\" ( HTAB / SP / VCHAR / obs-text )
```


> **SHOULD NOT**: A sender SHOULD NOT generate a quoted-pair in a quoted-string except
   where necessary to quote DQUOTE and backslash octets occurring within
> **SHOULD NOT**: that string.  A sender SHOULD NOT generate a quoted-pair in a comment
   except where necessary to quote parentheses ["(" and ")"] and
   backslash octets occurring within that comment.

### 5.6.5  Comments

   Comments can be included in some HTTP fields by surrounding the
   comment text with parentheses.  Comments are only allowed in fields
   containing "comment" as part of their field value definition.


```abnf
     comment        = "(" *( ctext / quoted-pair / comment ) ")"
     ctext          = HTAB / SP / %x21-27 / %x2A-5B / %x5D-7E / obs-text
```


### 5.6.6  Parameters

   Parameters are instances of name/value pairs; they are often used in
   field values as a common syntax for appending auxiliary information
   to an item.  Each parameter is usually delimited by an immediately
   preceding semicolon.


```abnf
     parameters      = *( OWS ";" OWS [ parameter ] )
     parameter       = parameter-name "=" parameter-value
     parameter-name  = token
     parameter-value = ( token / quoted-string )
```


   Parameter names are case-insensitive.  Parameter values might or
   might not be case-sensitive, depending on the semantics of the
   parameter name.  Examples of parameters and some equivalent forms can
   be seen in media types (Section 8.3.1) and the Accept header field
   (Section 12.5.1).

   A parameter value that matches the token production can be
   transmitted either as a token or within a quoted-string.  The quoted
   and unquoted values are equivalent.

      |  *Note:* Parameters do not allow whitespace (not even "bad"
      |  whitespace) around the "=" character.

### 5.6.7  Date/Time Formats

   Prior to 1995, there were three different formats commonly used by
   servers to communicate timestamps.  For compatibility with old
   implementations, all three are defined here.  The preferred format is
   a fixed-length and single-zone subset of the date and time
   specification used by the Internet Message Format [RFC5322].


```abnf
     HTTP-date    = IMF-fixdate / obs-date
```


   An example of the preferred format is

     Sun, 06 Nov 1994 08:49:37 GMT    ; IMF-fixdate

   Examples of the two obsolete formats are

     Sunday, 06-Nov-94 08:49:37 GMT   ; obsolete RFC 850 format
     Sun Nov  6 08:49:37 1994         ; ANSI C's asctime() format

> **MUST**: A recipient that parses a timestamp value in an HTTP field MUST
   accept all three HTTP-date formats.  When a sender generates a field
   that contains one or more timestamps defined as HTTP-date, the sender
> **MUST**: MUST generate those timestamps in the IMF-fixdate format.

   An HTTP-date value represents time as an instance of Coordinated
   Universal Time (UTC).  The first two formats indicate UTC by the
   three-letter abbreviation for Greenwich Mean Time, "GMT", a
   predecessor of the UTC name; values in the asctime format are assumed
   to be in UTC.

   A "clock" is an implementation capable of providing a reasonable
   approximation of the current instant in UTC.  A clock implementation
   ought to use NTP ([RFC5905]), or some similar protocol, to
   synchronize with UTC.

   Preferred format:


```abnf
     IMF-fixdate  = day-name "," SP date1 SP time-of-day SP GMT
```

     ; fixed length/zone/capitalization subset of the format
     ; see Section 3.3 of [RFC5322]


```abnf
     day-name     = %s"Mon" / %s"Tue" / %s"Wed"
                  / %s"Thu" / %s"Fri" / %s"Sat" / %s"Sun"

     date1        = day SP month SP year
                  ; e.g., 02 Jun 1982

     day          = 2DIGIT
     month        = %s"Jan" / %s"Feb" / %s"Mar" / %s"Apr"
                  / %s"May" / %s"Jun" / %s"Jul" / %s"Aug"
                  / %s"Sep" / %s"Oct" / %s"Nov" / %s"Dec"
     year         = 4DIGIT

     GMT          = %s"GMT"

     time-of-day  = hour ":" minute ":" second
                  ; 00:00:00 - 23:59:60 (leap second)

     hour         = 2DIGIT
     minute       = 2DIGIT
     second       = 2DIGIT
```


   Obsolete formats:


```abnf
     obs-date     = rfc850-date / asctime-date

     rfc850-date  = day-name-l "," SP date2 SP time-of-day SP GMT
     date2        = day "-" month "-" 2DIGIT
                  ; e.g., 02-Jun-82

     day-name-l   = %s"Monday" / %s"Tuesday" / %s"Wednesday"
                  / %s"Thursday" / %s"Friday" / %s"Saturday"
                  / %s"Sunday"

     asctime-date = day-name SP date3 SP time-of-day SP year
     date3        = month SP ( 2DIGIT / ( SP 1DIGIT ))
                  ; e.g., Jun  2
```


   HTTP-date is case sensitive.  Note that Section 4.2 of [CACHING]
   relaxes this for cache recipients.

> **MUST NOT**: A sender MUST NOT generate additional whitespace in an HTTP-date
   beyond that specifically included as SP in the grammar.  The
   semantics of day-name, day, month, year, and time-of-day are the same
   as those defined for the Internet Message Format constructs with the
   corresponding name ([RFC5322], Section 3.3).

   Recipients of a timestamp value in rfc850-date format, which uses a
> **MUST**: two-digit year, MUST interpret a timestamp that appears to be more
   than 50 years in the future as representing the most recent year in
   the past that had the same last two digits.

   Recipients of timestamp values are encouraged to be robust in parsing
   timestamps unless otherwise restricted by the field definition.  For
   example, messages are occasionally forwarded over HTTP from a non-
   HTTP source that might generate any of the date and time
   specifications defined by the Internet Message Format.

      |  *Note:* HTTP requirements for timestamp formats apply only to
      |  their usage within the protocol stream.  Implementations are
      |  not required to use these formats for user presentation,
      |  request logging, etc.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
