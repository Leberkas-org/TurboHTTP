---
title: "3.2.  Uniform Resource Identifiers"
rfc_number: 1945
rfc_section: "3.2"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 3.2: Uniform Resource Identifiers — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, uniform_resource_identifiers]
---

# 3.2.  Uniform Resource Identifiers

## 3.2  Uniform Resource Identifiers

   URIs have been known by many names: WWW addresses, Universal Document
   Identifiers, Universal Resource Identifiers [2], and finally the
   combination of Uniform Resource Locators (URL) [4] and Names (URN)
   [16]. As far as HTTP is concerned, Uniform Resource Identifiers are
   simply formatted strings which identify--via name, location, or any
   other characteristic--a network resource.

## 3.2.1  General Syntax

   URIs in HTTP can be represented in absolute form or relative to some
   known base URI [9], depending upon the context of their use. The two
   forms are differentiated by the fact that absolute URIs always begin
   with a scheme name followed by a colon.


```abnf
       URI            = ( absoluteURI | relativeURI ) [ "#" fragment ]

       absoluteURI    = scheme ":" *( uchar | reserved )

       relativeURI    = net_path | abs_path | rel_path
```


       net_path       = "//" net_loc [ abs_path ]
       abs_path       = "/" rel_path
       rel_path       = [ path ] [ ";" params ] [ "?" query ]


```abnf
       path           = fsegment *( "/" segment )
       fsegment       = 1*pchar
       segment        = *pchar

       params         = param *( ";" param )
       param          = *( pchar | "/" )

       scheme         = 1*( ALPHA | DIGIT | "+" | "-" | "." )
       net_loc        = *( pchar | ";" | "?" )
       query          = *( uchar | reserved )
       fragment       = *( uchar | reserved )

       pchar          = uchar | ":" | "@" | "&" | "=" | "+"
       uchar          = unreserved | escape
       unreserved     = ALPHA | DIGIT | safe | extra | national

       escape         = "%" HEX HEX
       reserved       = ";" | "/" | "?" | ":" | "@" | "&" | "=" | "+"
       extra          = "!" | "*" | "'" | "(" | ")" | ","
       safe           = "$" | "-" | "_" | "."
       unsafe         = CTL | SP | <"> | "#" | "%" | "<" | ">"
       national       = <any OCTET excluding ALPHA, DIGIT,
```




                        reserved, extra, safe, and unsafe>

   For definitive information on URL syntax and semantics, see RFC 1738
   [4] and RFC 1808 [9]. The BNF above includes national characters not
   allowed in valid URLs as specified by RFC 1738, since HTTP servers
   are not restricted in the set of unreserved characters allowed to
   represent the rel_path part of addresses, and HTTP proxies may
   receive requests for URIs not defined by RFC 1738.

3.2.2 http URL

   The "http" scheme is used to locate network resources via the HTTP
   protocol. This section defines the scheme-specific syntax and
   semantics for http URLs.

       http_URL       = "http:" "//" host [ ":" port ] [ abs_path ]


```abnf
       host           = <A legal Internet host domain name
                         or IP address (in dotted-decimal form),
                         as defined by Section 2.1 of RFC 1123>

       port           = *DIGIT
```


   If the port is empty or not given, port 80 is assumed. The semantics
   are that the identified resource is located at the server listening
   for TCP connections on that port of that host, and the Request-URI
   for the resource is abs_path. If the abs_path is not present in the
   URL, it must be given as "/" when used as a Request-URI (Section
   5.1.2).

      Note: Although the HTTP protocol is independent of the transport
      layer protocol, the http URL only identifies resources by their
      TCP location, and thus non-TCP resources must be identified by
      some other URI scheme.

   The canonical form for "http" URLs is obtained by converting any
   UPALPHA characters in host to their LOALPHA equivalent (hostnames are
   case-insensitive), eliding the [ ":" port ] if the port is 80, and
   replacing an empty abs_path with "/".

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
