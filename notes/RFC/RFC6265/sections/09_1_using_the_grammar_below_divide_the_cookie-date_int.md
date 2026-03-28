---
title: "1.  Using the grammar below, divide the cookie-date into date-tokens."
rfc_number: 6265
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 1: Using the grammar below, divide the cookie-date into date-tokens. — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, using_the_grammar_below_divide_the_cookie-date_int]
---

# 1.  Using the grammar below, divide the cookie-date into date-tokens.



```abnf
   cookie-date     = *delimiter date-token-list *delimiter
   date-token-list = date-token *( 1*delimiter date-token )
   date-token      = 1*non-delimiter

   delimiter       = %x09 / %x20-2F / %x3B-40 / %x5B-60 / %x7B-7E
   non-delimiter   = %x00-08 / %x0A-1F / DIGIT / ":" / ALPHA / %x7F-FF
   non-digit       = %x00-2F / %x3A-FF

   day-of-month    = 1*2DIGIT ( non-digit *OCTET )
   month           = ( "jan" / "feb" / "mar" / "apr" /
                       "may" / "jun" / "jul" / "aug" /
                       "sep" / "oct" / "nov" / "dec" ) *OCTET
   year            = 2*4DIGIT ( non-digit *OCTET )
   time            = hms-time ( non-digit *OCTET )
   hms-time        = time-field ":" time-field ":" time-field
   time-field      = 1*2DIGIT
```


   2.  Process each date-token sequentially in the order the date-tokens
       appear in the cookie-date:






       1.  If the found-time flag is not set and the token matches the
           time production, set the found-time flag and set the hour-
           value, minute-value, and second-value to the numbers denoted
           by the digits in the date-token, respectively.  Skip the
           remaining sub-steps and continue to the next date-token.

       2.  If the found-day-of-month flag is not set and the date-token
           matches the day-of-month production, set the found-day-of-
           month flag and set the day-of-month-value to the number
           denoted by the date-token.  Skip the remaining sub-steps and
           continue to the next date-token.

       3.  If the found-month flag is not set and the date-token matches
           the month production, set the found-month flag and set the
           month-value to the month denoted by the date-token.  Skip the
           remaining sub-steps and continue to the next date-token.

       4.  If the found-year flag is not set and the date-token matches
           the year production, set the found-year flag and set the
           year-value to the number denoted by the date-token.  Skip the
           remaining sub-steps and continue to the next date-token.

   3.  If the year-value is greater than or equal to 70 and less than or
       equal to 99, increment the year-value by 1900.

   4.  If the year-value is greater than or equal to 0 and less than or
       equal to 69, increment the year-value by 2000.

       1.  NOTE: Some existing user agents interpret two-digit years
           differently.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
