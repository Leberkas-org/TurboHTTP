---
title: "7.  Return to Step 1 of this algorithm."
rfc_number: 6265
rfc_section: "7"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 7: Return to Step 1 of this algorithm. — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, return_to_step_1_of_this_algorithm]
---

# 7.  Return to Step 1 of this algorithm.


   When the user agent finishes parsing the set-cookie-string, the user
   agent is said to "receive a cookie" from the request-uri with name
   cookie-name, value cookie-value, and attributes cookie-attribute-
   list.  (See Section 5.3 for additional requirements triggered by
   receiving a cookie.)

### 5.2.1.  The Expires Attribute

   If the attribute-name case-insensitively matches the string
> **MUST**: "Expires", the user agent MUST process the cookie-av as follows.

   Let the expiry-time be the result of parsing the attribute-value as
   cookie-date (see Section 5.1.1).

   If the attribute-value failed to parse as a cookie date, ignore the
   cookie-av.

   If the expiry-time is later than the last date the user agent can
> **MAY**: represent, the user agent MAY replace the expiry-time with the last
   representable date.



   If the expiry-time is earlier than the earliest date the user agent
> **MAY**: can represent, the user agent MAY replace the expiry-time with the
   earliest representable date.

   Append an attribute to the cookie-attribute-list with an attribute-
   name of Expires and an attribute-value of expiry-time.

### 5.2.2.  The Max-Age Attribute

   If the attribute-name case-insensitively matches the string "Max-
> **MUST**: Age", the user agent MUST process the cookie-av as follows.

   If the first character of the attribute-value is not a DIGIT or a "-"
   character, ignore the cookie-av.

   If the remainder of attribute-value contains a non-DIGIT character,
   ignore the cookie-av.

   Let delta-seconds be the attribute-value converted to an integer.

   If delta-seconds is less than or equal to zero (0), let expiry-time
   be the earliest representable date and time.  Otherwise, let the
   expiry-time be the current date and time plus delta-seconds seconds.

   Append an attribute to the cookie-attribute-list with an attribute-
   name of Max-Age and an attribute-value of expiry-time.

### 5.2.3.  The Domain Attribute

   If the attribute-name case-insensitively matches the string "Domain",
> **MUST**: the user agent MUST process the cookie-av as follows.

   If the attribute-value is empty, the behavior is undefined.  However,
> **SHOULD**: the user agent SHOULD ignore the cookie-av entirely.

   If the first character of the attribute-value string is %x2E ("."):

      Let cookie-domain be the attribute-value without the leading %x2E
      (".") character.

   Otherwise:

      Let cookie-domain be the entire attribute-value.

   Convert the cookie-domain to lower case.

   Append an attribute to the cookie-attribute-list with an attribute-
   name of Domain and an attribute-value of cookie-domain.



### 5.2.4.  The Path Attribute

   If the attribute-name case-insensitively matches the string "Path",
> **MUST**: the user agent MUST process the cookie-av as follows.

   If the attribute-value is empty or if the first character of the
   attribute-value is not %x2F ("/"):

      Let cookie-path be the default-path.

   Otherwise:

      Let cookie-path be the attribute-value.

   Append an attribute to the cookie-attribute-list with an attribute-
   name of Path and an attribute-value of cookie-path.

### 5.2.5.  The Secure Attribute

   If the attribute-name case-insensitively matches the string "Secure",
> **MUST**: the user agent MUST append an attribute to the cookie-attribute-list
   with an attribute-name of Secure and an empty attribute-value.

### 5.2.6.  The HttpOnly Attribute

   If the attribute-name case-insensitively matches the string
> **MUST**: "HttpOnly", the user agent MUST append an attribute to the cookie-
   attribute-list with an attribute-name of HttpOnly and an empty
   attribute-value.

## 5.3.  Storage Model

   The user agent stores the following fields about each cookie: name,
   value, expiry-time, domain, path, creation-time, last-access-time,
   persistent-flag, host-only-flag, secure-only-flag, and http-only-
   flag.

   When the user agent "receives a cookie" from a request-uri with name
   cookie-name, value cookie-value, and attributes cookie-attribute-
> **MUST**: list, the user agent MUST process the cookie as follows:

> **MAY**: 1.   A user agent MAY ignore a received cookie in its entirety.  For
        example, the user agent might wish to block receiving cookies
        from "third-party" responses or the user agent might not wish to
        store cookies that exceed some size.






   2.   Create a new cookie with name cookie-name, value cookie-value.
        Set the creation-time and the last-access-time to the current
        date and time.

   3.   If the cookie-attribute-list contains an attribute with an
        attribute-name of "Max-Age":

           Set the cookie's persistent-flag to true.

           Set the cookie's expiry-time to attribute-value of the last
           attribute in the cookie-attribute-list with an attribute-name
           of "Max-Age".

        Otherwise, if the cookie-attribute-list contains an attribute
        with an attribute-name of "Expires" (and does not contain an
        attribute with an attribute-name of "Max-Age"):

           Set the cookie's persistent-flag to true.

           Set the cookie's expiry-time to attribute-value of the last
           attribute in the cookie-attribute-list with an attribute-name
           of "Expires".

        Otherwise:

           Set the cookie's persistent-flag to false.

           Set the cookie's expiry-time to the latest representable
           date.

   4.   If the cookie-attribute-list contains an attribute with an
        attribute-name of "Domain":

           Let the domain-attribute be the attribute-value of the last
           attribute in the cookie-attribute-list with an attribute-name
           of "Domain".

        Otherwise:

           Let the domain-attribute be the empty string.

   5.   If the user agent is configured to reject "public suffixes" and
        the domain-attribute is a public suffix:

           If the domain-attribute is identical to the canonicalized
           request-host:

              Let the domain-attribute be the empty string.



           Otherwise:

              Ignore the cookie entirely and abort these steps.

           NOTE: A "public suffix" is a domain that is controlled by a
           public registry, such as "com", "co.uk", and "pvt.k12.wy.us".
           This step is essential for preventing attacker.com from
           disrupting the integrity of example.com by setting a cookie
           with a Domain attribute of "com".  Unfortunately, the set of
           public suffixes (also known as "registry controlled domains")
> **SHOULD**: changes over time.  If feasible, user agents SHOULD use an
           up-to-date public suffix list, such as the one maintained by
           the Mozilla project at <http://publicsuffix.org/>.

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
