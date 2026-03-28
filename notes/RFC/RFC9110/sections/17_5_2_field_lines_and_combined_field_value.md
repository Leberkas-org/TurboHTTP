---
title: "5.2.  Field Lines and Combined Field Value"
rfc_number: 9110
rfc_section: "5.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 5.2: Field Lines and Combined Field Value — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, field_lines_and_combined_field_value]
---

## 5.2.  Field Lines and Combined Field Value

## 5.2  Field Lines and Combined Field Value

   Field sections are composed of any number of "field lines", each with
   a "field name" (see Section 5.1) identifying the field, and a "field
   line value" that conveys data for that instance of the field.

   When a field name is only present once in a section, the combined
   "field value" for that field consists of the corresponding field line
   value.  When a field name is repeated within a section, its combined
   field value consists of the list of corresponding field line values
   within that section, concatenated in order, with each field line
   value separated by a comma.

   For example, this section:

   Example-Field: Foo, Bar
   Example-Field: Baz

   contains two field lines, both with the field name "Example-Field".
   The first field line has a field line value of "Foo, Bar", while the
   second field line value is "Baz".  The field value for "Example-
   Field" is the list "Foo, Bar, Baz".

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
