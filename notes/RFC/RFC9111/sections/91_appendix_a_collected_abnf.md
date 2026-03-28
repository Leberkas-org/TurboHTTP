---
title: "Appendix A.  Collected ABNF"
rfc_number: 9111
rfc_section: "Appendix A"
source_url: "https://www.rfc-editor.org/rfc/rfc9111"
description: "Appendix A: Collected ABNF — RFC 9111 — HTTP Caching"
tags: [RFC9111, HTTP-caching, freshness, validation, Cache-Control, max-age, Expires, conditional-requests, Vary, collected_abnf]
---

## Appendix A.  Collected ABNF

Appendix A.  Collected ABNF

   In the collected ABNF below, list rules are expanded per
   Section 5.6.1 of [HTTP].


```abnf
   Age = delta-seconds

   Cache-Control = [ cache-directive *( OWS "," OWS cache-directive ) ]

   Expires = HTTP-date

   HTTP-date = <HTTP-date, see [HTTP], Section 5.6.7>

   OWS = <OWS, see [HTTP], Section 5.6.3>

   cache-directive = token [ "=" ( token / quoted-string ) ]

   delta-seconds = 1*DIGIT

   field-name = <field-name, see [HTTP], Section 5.1>

   quoted-string = <quoted-string, see [HTTP], Section 5.6.4>

   token = <token, see [HTTP], Section 5.6.2>
```

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
