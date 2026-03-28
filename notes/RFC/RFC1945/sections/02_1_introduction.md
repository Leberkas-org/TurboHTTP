---
title: "1.  Introduction"
rfc_number: 1945
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 1: Introduction — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, introduction]
---

# 1.  Introduction


## 1.1  Purpose

   The Hypertext Transfer Protocol (HTTP) is an application-level
   protocol with the lightness and speed necessary for distributed,
   collaborative, hypermedia information systems. HTTP has been in use
   by the World-Wide Web global information initiative since 1990. This
   specification reflects common usage of the protocol referred too as
   "HTTP/1.0". This specification describes the features that seem to be
   consistently implemented in most HTTP/1.0 clients and servers. The
   specification is split into two sections. Those features of HTTP for
   which implementations are usually consistent are described in the
   main body of this document. Those features which have few or
   inconsistent implementations are listed in Appendix D.

   Practical information systems require more functionality than simple
   retrieval, including search, front-end update, and annotation. HTTP
   allows an open-ended set of methods to be used to indicate the
   purpose of a request. It builds on the discipline of reference
   provided by the Uniform Resource Identifier (URI) [2], as a location
   (URL) [4] or name (URN) [16], for indicating the resource on which a
   method is to be applied. Messages are passed in a format similar to
   that used by Internet Mail [7] and the Multipurpose Internet Mail
   Extensions (MIME) [5].

   HTTP is also used as a generic protocol for communication between
   user agents and proxies/gateways to other Internet protocols, such as
   SMTP [12], NNTP [11], FTP [14], Gopher [1], and WAIS [8], allowing
   basic hypermedia access to resources available from diverse
   applications and simplifying the implementation of user agents.

## 1.2  Terminology

   This specification uses a number of terms to refer to the roles
   played by participants in, and objects of, the HTTP communication.

   connection

       A transport layer virtual circuit established between two
       application programs for the purpose of communication.

   message

       The basic unit of HTTP communication, consisting of a structured
       sequence of octets matching the syntax defined in Section 4 and
       transmitted via the connection.




   request

       An HTTP request message (as defined in Section 5).

   response

       An HTTP response message (as defined in Section 6).

   resource

       A network data object or service which can be identified by a
       URI (Section 3.2).

   entity

       A particular representation or rendition of a data resource, or
       reply from a service resource, that may be enclosed within a
       request or response message. An entity consists of
       metainformation in the form of entity headers and content in the
       form of an entity body.

   client

       An application program that establishes connections for the
       purpose of sending requests.

   user agent

       The client which initiates a request. These are often browsers,
       editors, spiders (web-traversing robots), or other end user
       tools.

   server

       An application program that accepts connections in order to
       service requests by sending back responses.

   origin server

       The server on which a given resource resides or is to be created.

   proxy

       An intermediary program which acts as both a server and a client
       for the purpose of making requests on behalf of other clients.
       Requests are serviced internally or by passing them, with
       possible translation, on to other servers. A proxy must
       interpret and, if necessary, rewrite a request message before



       forwarding it. Proxies are often used as client-side portals
       through network firewalls and as helper applications for
       handling requests via protocols not implemented by the user
       agent.

   gateway

       A server which acts as an intermediary for some other server.
       Unlike a proxy, a gateway receives requests as if it were the
       origin server for the requested resource; the requesting client
       may not be aware that it is communicating with a gateway.
       Gateways are often used as server-side portals through network
       firewalls and as protocol translators for access to resources
       stored on non-HTTP systems.

   tunnel

       A tunnel is an intermediary program which is acting as a blind
       relay between two connections. Once active, a tunnel is not
       considered a party to the HTTP communication, though the tunnel
       may have been initiated by an HTTP request. The tunnel ceases to
       exist when both ends of the relayed connections are closed.
       Tunnels are used when a portal is necessary and the intermediary
       cannot, or should not, interpret the relayed communication.

   cache

       A program's local store of response messages and the subsystem
       that controls its message storage, retrieval, and deletion. A
       cache stores cachable responses in order to reduce the response
       time and network bandwidth consumption on future, equivalent
       requests. Any client or server may include a cache, though a
       cache cannot be used by a server while it is acting as a tunnel.

   Any given program may be capable of being both a client and a server;
   our use of these terms refers only to the role being performed by the
   program for a particular connection, rather than to the program's
   capabilities in general. Likewise, any server may act as an origin
   server, proxy, gateway, or tunnel, switching behavior based on the
   nature of each request.

## 1.3  Overall Operation

   The HTTP protocol is based on a request/response paradigm. A client
   establishes a connection with a server and sends a request to the
   server in the form of a request method, URI, and protocol version,
   followed by a MIME-like message containing request modifiers, client
   information, and possible body content. The server responds with a



   status line, including the message's protocol version and a success
   or error code, followed by a MIME-like message containing server
   information, entity metainformation, and possible body content.

   Most HTTP communication is initiated by a user agent and consists of
   a request to be applied to a resource on some origin server. In the
   simplest case, this may be accomplished via a single connection (v)
   between the user agent (UA) and the origin server (O).

          request chain ------------------------>
       UA -------------------v------------------- O
          <----------------------- response chain

   A more complicated situation occurs when one or more intermediaries
   are present in the request/response chain. There are three common
   forms of intermediary: proxy, gateway, and tunnel. A proxy is a
   forwarding agent, receiving requests for a URI in its absolute form,
   rewriting all or parts of the message, and forwarding the reformatted
   request toward the server identified by the URI. A gateway is a
   receiving agent, acting as a layer above some other server(s) and, if
   necessary, translating the requests to the underlying server's
   protocol. A tunnel acts as a relay point between two connections
   without changing the messages; tunnels are used when the
   communication needs to pass through an intermediary (such as a
   firewall) even when the intermediary cannot understand the contents
   of the messages.

          request chain -------------------------------------->
       UA -----v----- A -----v----- B -----v----- C -----v----- O
          <------------------------------------- response chain

   The figure above shows three intermediaries (A, B, and C) between the
   user agent and origin server. A request or response message that
   travels the whole chain must pass through four separate connections.
   This distinction is important because some HTTP communication options
   may apply only to the connection with the nearest, non-tunnel
   neighbor, only to the end-points of the chain, or to all connections
   along the chain. Although the diagram is linear, each participant may
   be engaged in multiple, simultaneous communications. For example, B
   may be receiving requests from many clients other than A, and/or
   forwarding requests to servers other than C, at the same time that it
   is handling A's request.

   Any party to the communication which is not acting as a tunnel may
   employ an internal cache for handling requests. The effect of a cache
   is that the request/response chain is shortened if one of the
   participants along the chain has a cached response applicable to that
   request. The following illustrates the resulting chain if B has a



   cached copy of an earlier response from O (via C) for a request which
   has not been cached by UA or A.

          request chain ---------->
       UA -----v----- A -----v----- B - - - - - - C - - - - - - O
          <--------- response chain

   Not all responses are cachable, and some requests may contain
   modifiers which place special requirements on cache behavior. Some
   HTTP/1.0 applications use heuristics to describe what is or is not a
   "cachable" response, but these rules are not standardized.

   On the Internet, HTTP communication generally takes place over TCP/IP
   connections. The default port is TCP 80 [15], but other ports can be
   used. This does not preclude HTTP from being implemented on top of
   any other protocol on the Internet, or on other networks. HTTP only
   presumes a reliable transport; any protocol that provides such
   guarantees can be used, and the mapping of the HTTP/1.0 request and
   response structures onto the transport data units of the protocol in
   question is outside the scope of this specification.

   Except for experimental applications, current practice requires that
   the connection be established by the client prior to each request and
   closed by the server after sending the response. Both clients and
   servers should be aware that either party may close the connection
   prematurely, due to user action, automated time-out, or program
   failure, and should handle such closing in a predictable fashion. In
   any case, the closing of the connection by either or both parties
   always terminates the current request, regardless of its status.

## 1.4  HTTP and MIME

   HTTP/1.0 uses many of the constructs defined for MIME, as defined in
   RFC 1521 [5]. Appendix C describes the ways in which the context of
   HTTP allows for different use of Internet Media Types than is
   typically found in Internet mail, and gives the rationale for those
   differences.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
