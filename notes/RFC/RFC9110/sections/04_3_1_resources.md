---
title: "3.1.  Resources"
rfc_number: 9110
rfc_section: "3.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3.1: Resources — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, resources]
---

## 3.1.  Resources

3.  Terminology and Core Concepts

   HTTP was created for the World Wide Web (WWW) architecture and has
   evolved over time to support the scalability needs of a worldwide
   hypertext system.  Much of that architecture is reflected in the
   terminology used to define HTTP.

## 3.1  Resources

   The target of an HTTP request is called a "resource".  HTTP does not
   limit the nature of a resource; it merely defines an interface that
   might be used to interact with resources.  Most resources are
   identified by a Uniform Resource Identifier (URI), as described in
   Section 4.

   One design goal of HTTP is to separate resource identification from
   request semantics, which is made possible by vesting the request
   semantics in the request method (Section 9) and a few request-
   modifying header fields.  A resource cannot treat a request in a
   manner inconsistent with the semantics of the method of the request.
   For example, though the URI of a resource might imply semantics that
   are not safe, a client can expect the resource to avoid actions that
   are unsafe when processing a request with a safe method (see
   Section 9.2.1).

   HTTP relies upon the Uniform Resource Identifier (URI) standard [URI]
   to indicate the target resource (Section 7.1) and relationships
   between resources.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
