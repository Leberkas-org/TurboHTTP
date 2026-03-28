---
title: "1.  Introduction"
rfc_number: 9110
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 1: Introduction — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, introduction]
---

## 1.  Introduction

1.  Introduction

## 1.1  Purpose

   The Hypertext Transfer Protocol (HTTP) is a family of stateless,
   application-level, request/response protocols that share a generic
   interface, extensible semantics, and self-descriptive messages to
   enable flexible interaction with network-based hypertext information
   systems.

   HTTP hides the details of how a service is implemented by presenting
   a uniform interface to clients that is independent of the types of
   resources provided.  Likewise, servers do not need to be aware of
   each client's purpose: a request can be considered in isolation
   rather than being associated with a specific type of client or a
   predetermined sequence of application steps.  This allows general-
   purpose implementations to be used effectively in many different
   contexts, reduces interaction complexity, and enables independent
   evolution over time.

   HTTP is also designed for use as an intermediation protocol, wherein
   proxies and gateways can translate non-HTTP information systems into
   a more generic interface.

   One consequence of this flexibility is that the protocol cannot be
   defined in terms of what occurs behind the interface.  Instead, we
   are limited to defining the syntax of communication, the intent of
   received communication, and the expected behavior of recipients.  If
   the communication is considered in isolation, then successful actions
   ought to be reflected in corresponding changes to the observable
   interface provided by servers.  However, since multiple clients might
   act in parallel and perhaps at cross-purposes, we cannot require that
   such changes be observable beyond the scope of a single response.

## 1.2  History and Evolution

   HTTP has been the primary information transfer protocol for the World
   Wide Web since its introduction in 1990.  It began as a trivial
   mechanism for low-latency requests, with a single method (GET) to
   request transfer of a presumed hypertext document identified by a
   given pathname.  As the Web grew, HTTP was extended to enclose
   requests and responses within messages, transfer arbitrary data
   formats using MIME-like media types, and route requests through
   intermediaries.  These protocols were eventually defined as HTTP/0.9
   and HTTP/1.0 (see [HTTP/1.0]).

   HTTP/1.1 was designed to refine the protocol's features while
   retaining compatibility with the existing text-based messaging
   syntax, improving its interoperability, scalability, and robustness
   across the Internet.  This included length-based data delimiters for
   both fixed and dynamic (chunked) content, a consistent framework for
   content negotiation, opaque validators for conditional requests,
   cache controls for better cache consistency, range requests for
   partial updates, and default persistent connections.  HTTP/1.1 was
   introduced in 1995 and published on the Standards Track in 1997
   [RFC2068], revised in 1999 [RFC2616], and revised again in 2014
   ([RFC7230] through [RFC7235]).

   HTTP/2 ([HTTP/2]) introduced a multiplexed session layer on top of
   the existing TLS and TCP protocols for exchanging concurrent HTTP
   messages with efficient field compression and server push.  HTTP/3
   ([HTTP/3]) provides greater independence for concurrent messages by
   using QUIC as a secure multiplexed transport over UDP instead of TCP.

   All three major versions of HTTP rely on the semantics defined by
   this document.  They have not obsoleted each other because each one
   has specific benefits and limitations depending on the context of
   use.  Implementations are expected to choose the most appropriate
   transport and messaging syntax for their particular context.

   This revision of HTTP separates the definition of semantics (this
   document) and caching ([CACHING]) from the current HTTP/1.1 messaging
   syntax ([HTTP/1.1]) to allow each major protocol version to progress
   independently while referring to the same core semantics.

## 1.3  Core Semantics

   HTTP provides a uniform interface for interacting with a resource
   (Section 3.1) -- regardless of its type, nature, or implementation --
   by sending messages that manipulate or transfer representations
   (Section 3.2).

   Each message is either a request or a response.  A client constructs
   request messages that communicate its intentions and routes those
   messages toward an identified origin server.  A server listens for
   requests, parses each message received, interprets the message
   semantics in relation to the identified target resource, and responds
   to that request with one or more response messages.  The client
   examines received responses to see if its intentions were carried
   out, determining what to do next based on the status codes and
   content received.

   HTTP semantics include the intentions defined by each request method
   (Section 9), extensions to those semantics that might be described in
   request header fields, status codes that describe the response
   (Section 15), and other control data and resource metadata that might
   be given in response fields.

   Semantics also include representation metadata that describe how
   content is intended to be interpreted by a recipient, request header
   fields that might influence content selection, and the various
   selection algorithms that are collectively referred to as "content
   negotiation" (Section 12).

## 1.4  Specifications Obsoleted by This Document

   +============================================+===========+=====+
   | Title                                      | Reference | See |
   +============================================+===========+=====+
   | HTTP Over TLS                              | [RFC2818] | B.1 |
   +--------------------------------------------+-----------+-----+
   | HTTP/1.1 Message Syntax and Routing [*]    | [RFC7230] | B.2 |
   +--------------------------------------------+-----------+-----+
   | HTTP/1.1 Semantics and Content             | [RFC7231] | B.3 |
   +--------------------------------------------+-----------+-----+
   | HTTP/1.1 Conditional Requests              | [RFC7232] | B.4 |
   +--------------------------------------------+-----------+-----+
   | HTTP/1.1 Range Requests                    | [RFC7233] | B.5 |
   +--------------------------------------------+-----------+-----+
   | HTTP/1.1 Authentication                    | [RFC7235] | B.6 |
   +--------------------------------------------+-----------+-----+
   | HTTP Status Code 308 (Permanent Redirect)  | [RFC7538] | B.7 |
   +--------------------------------------------+-----------+-----+
   | HTTP Authentication-Info and Proxy-        | [RFC7615] | B.8 |
   | Authentication-Info Response Header Fields |           |     |
   +--------------------------------------------+-----------+-----+
   | HTTP Client-Initiated Content-Encoding     | [RFC7694] | B.9 |
   +--------------------------------------------+-----------+-----+

                               Table 1

   [*] This document only obsoletes the portions of RFC 7230 that are
   independent of the HTTP/1.1 messaging syntax and connection
   management; the remaining bits of RFC 7230 are obsoleted by
   "HTTP/1.1" [HTTP/1.1].

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
