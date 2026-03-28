---
title: "Appendix A.  Collected ABNF"
rfc_number: 9112
rfc_section: "Appendix A"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Appendix A: Collected ABNF — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, collected_abnf]
---

## Appendix A.  Collected ABNF

Appendix A.  Collected ABNF

   In the collected ABNF below, list rules are expanded per
   Section 5.6.1 of [HTTP].


```abnf
   BWS = <BWS, see [HTTP], Section 5.6.3>

   HTTP-message = start-line CRLF *( field-line CRLF ) CRLF [
```

    message-body ]

```abnf
   HTTP-name = %x48.54.54.50 ; HTTP
   HTTP-version = HTTP-name "/" DIGIT "." DIGIT

   OWS = <OWS, see [HTTP], Section 5.6.3>

   RWS = <RWS, see [HTTP], Section 5.6.3>

   Transfer-Encoding = [ transfer-coding *( OWS "," OWS transfer-coding
```

    ) ]


```abnf
   absolute-URI = <absolute-URI, see [URI], Section 4.3>
   absolute-form = absolute-URI
   absolute-path = <absolute-path, see [HTTP], Section 4.1>
   asterisk-form = "*"
   authority = <authority, see [URI], Section 3.2>
   authority-form = uri-host ":" port

   chunk = chunk-size [ chunk-ext ] CRLF chunk-data CRLF
   chunk-data = 1*OCTET
   chunk-ext = *( BWS ";" BWS chunk-ext-name [ BWS "=" BWS chunk-ext-val
```

    ] )

```abnf
   chunk-ext-name = token
   chunk-ext-val = token / quoted-string
   chunk-size = 1*HEXDIG
   chunked-body = *chunk last-chunk trailer-section CRLF

   field-line = field-name ":" OWS field-value OWS
   field-name = <field-name, see [HTTP], Section 5.1>
   field-value = <field-value, see [HTTP], Section 5.5>

   last-chunk = 1*"0" [ chunk-ext ] CRLF

   message-body = *OCTET
   method = token

   obs-fold = OWS CRLF RWS
   obs-text = <obs-text, see [HTTP], Section 5.6.4>
   origin-form = absolute-path [ "?" query ]

   port = <port, see [URI], Section 3.2.3>

   query = <query, see [URI], Section 3.4>
   quoted-string = <quoted-string, see [HTTP], Section 5.6.4>

   reason-phrase = 1*( HTAB / SP / VCHAR / obs-text )
   request-line = method SP request-target SP HTTP-version
   request-target = origin-form / absolute-form / authority-form /
    asterisk-form

   start-line = request-line / status-line
   status-code = 3DIGIT
   status-line = HTTP-version SP status-code SP [ reason-phrase ]

   token = <token, see [HTTP], Section 5.6.2>
   trailer-section = *( field-line CRLF )
   transfer-coding = <transfer-coding, see [HTTP], Section 10.1.4>

   uri-host = <host, see [URI], Section 3.2.2>
```

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
