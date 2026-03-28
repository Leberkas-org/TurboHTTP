---
title: "12.  Security Considerations"
rfc_number: 1945
rfc_section: "12"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 12: Security Considerations — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, security_considerations]
---

# 12.  Security Considerations


   This section is meant to inform application developers, information
   providers, and users of the security limitations in HTTP/1.0 as
   described by this document. The discussion does not include
   definitive solutions to the problems revealed, though it does make
   some suggestions for reducing security risks.

## 12.1  Authentication of Clients

   As mentioned in Section 11.1, the Basic authentication scheme is not
   a secure method of user authentication, nor does it prevent the
   Entity-Body from being transmitted in clear text across the physical
   network used as the carrier. HTTP/1.0 does not prevent additional
   authentication schemes and encryption mechanisms from being employed
   to increase security.

## 12.2  Safe Methods

   The writers of client software should be aware that the software
   represents the user in their interactions over the Internet, and
   should be careful to allow the user to be aware of any actions they
   may take which may have an unexpected significance to themselves or
   others.

   In particular, the convention has been established that the GET and
   HEAD methods should never have the significance of taking an action
   other than retrieval. These methods should be considered "safe." This
   allows user agents to represent other methods, such as POST, in a
   special way, so that the user is made aware of the fact that a
   possibly unsafe action is being requested.





   Naturally, it is not possible to ensure that the server does not
   generate side-effects as a result of performing a GET request; in
   fact, some dynamic resources consider that a feature. The important
   distinction here is that the user did not request the side-effects,
   so therefore cannot be held accountable for them.

## 12.3  Abuse of Server Log Information

   A server is in the position to save personal data about a user's
   requests which may identify their reading patterns or subjects of
   interest. This information is clearly confidential in nature and its
   handling may be constrained by law in certain countries. People using
   the HTTP protocol to provide data are responsible for ensuring that
   such material is not distributed without the permission of any
   individuals that are identifiable by the published results.

## 12.4  Transfer of Sensitive Information

   Like any generic data transfer protocol, HTTP cannot regulate the
   content of the data that is transferred, nor is there any a priori
   method of determining the sensitivity of any particular piece of
   information within the context of any given request. Therefore,
   applications should supply as much control over this information as
   possible to the provider of that information. Three header fields are
   worth special mention in this context: Server, Referer and From.

   Revealing the specific software version of the server may allow the
   server machine to become more vulnerable to attacks against software
   that is known to contain security holes. Implementors should make the
   Server header field a configurable option.

   The Referer field allows reading patterns to be studied and reverse
   links drawn. Although it can be very useful, its power can be abused
   if user details are not separated from the information contained in
   the Referer. Even when the personal information has been removed, the
   Referer field may indicate a private document's URI whose publication
   would be inappropriate.

   The information sent in the From field might conflict with the user's
   privacy interests or their site's security policy, and hence it
   should not be transmitted without the user being able to disable,
   enable, and modify the contents of the field. The user must be able
   to set the contents of this field within a user preference or
   application defaults configuration.

   We suggest, though do not require, that a convenient toggle interface
   be provided for the user to enable or disable the sending of From and
   Referer information.



## 12.5  Attacks Based On File and Path Names

   Implementations of HTTP origin servers should be careful to restrict
   the documents returned by HTTP requests to be only those that were
   intended by the server administrators. If an HTTP server translates
   HTTP URIs directly into file system calls, the server must take
   special care not to serve files that were not intended to be
   delivered to HTTP clients. For example, Unix, Microsoft Windows, and
   other operating systems use ".." as a path component to indicate a
   directory level above the current one. On such a system, an HTTP
   server must disallow any such construct in the Request-URI if it
   would otherwise allow access to a resource outside those intended to
   be accessible via the HTTP server. Similarly, files intended for
   reference only internally to the server (such as access control
   files, configuration files, and script code) must be protected from
   inappropriate retrieval, since they might contain sensitive
   information. Experience has shown that minor bugs in such HTTP server
   implementations have turned into security risks.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
