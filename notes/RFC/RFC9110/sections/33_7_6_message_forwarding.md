---
title: "7.6.  Message Forwarding"
rfc_number: 9110
rfc_section: "7.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 7.6: Message Forwarding — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, message_forwarding]
---

## 7.6.  Message Forwarding

## 7.6  Message Forwarding

   As described in Section 3.7, intermediaries can serve a variety of
   roles in the processing of HTTP requests and responses.  Some
   intermediaries are used to improve performance or availability.
   Others are used for access control or to filter content.  Since an
   HTTP stream has characteristics similar to a pipe-and-filter
   architecture, there are no inherent limits to the extent an
   intermediary can enhance (or interfere) with either direction of the
   stream.

   Intermediaries are expected to forward messages even when protocol
   elements are not recognized (e.g., new methods, status codes, or
   field names) since that preserves extensibility for downstream
   recipients.

> **MUST**: An intermediary not acting as a tunnel MUST implement the Connection
   header field, as specified in Section 7.6.1, and exclude fields from
   being forwarded that are only intended for the incoming connection.

> **MUST NOT**: An intermediary MUST NOT forward a message to itself unless it is
   protected from an infinite request loop.  In general, an intermediary
   ought to recognize its own server names, including any aliases, local
   variations, or literal IP addresses, and respond to such requests
   directly.

   An HTTP message can be parsed as a stream for incremental processing
   or forwarding downstream.  However, senders and recipients cannot
   rely on incremental delivery of partial messages, since some
   implementations will buffer or delay message forwarding for the sake
   of network efficiency, security checks, or content transformations.

### 7.6.1  Connection

   The "Connection" header field allows the sender to list desired
   control options for the current connection.


```abnf
     Connection        = #connection-option
     connection-option = token
```


   Connection options are case-insensitive.

   When a field aside from Connection is used to supply control
> **MUST**: information for or about the current connection, the sender MUST list
   the corresponding field name within the Connection header field.
   Note that some versions of HTTP prohibit the use of fields for such
   information, and therefore do not allow the Connection field.

> **MUST**: Intermediaries MUST parse a received Connection header field before a
   message is forwarded and, for each connection-option in this field,
   remove any header or trailer field(s) from the message with the same
   name as the connection-option, and then remove the Connection header
   field itself (or replace it with the intermediary's own control
   options for the forwarded message).

   Hence, the Connection header field provides a declarative way of
   distinguishing fields that are only intended for the immediate
   recipient ("hop-by-hop") from those fields that are intended for all
   recipients on the chain ("end-to-end"), enabling the message to be
   self-descriptive and allowing future connection-specific extensions
   to be deployed without fear that they will be blindly forwarded by
   older intermediaries.

> **SHOULD**: Furthermore, intermediaries SHOULD remove or replace fields that are
   known to require removal before forwarding, whether or not they
   appear as a connection-option, after applying those fields'
   semantics.  This includes but is not limited to:

   *  Proxy-Connection (Appendix C.2.2 of [HTTP/1.1])

   *  Keep-Alive (Section 19.7.1 of [RFC2068])

   *  TE (Section 10.1.4)

   *  Transfer-Encoding (Section 6.1 of [HTTP/1.1])

   *  Upgrade (Section 7.8)

> **MUST NOT**: A sender MUST NOT send a connection option corresponding to a field
   that is intended for all recipients of the content.  For example,
   Cache-Control is never appropriate as a connection option
   (Section 5.2 of [CACHING]).

   Connection options do not always correspond to a field present in the
   message, since a connection-specific field might not be needed if
   there are no parameters associated with a connection option.  In
   contrast, a connection-specific field received without a
   corresponding connection option usually indicates that the field has
   been improperly forwarded by an intermediary and ought to be ignored
   by the recipient.

   When defining a new connection option that does not correspond to a
   field, specification authors ought to reserve the corresponding field
   name anyway in order to avoid later collisions.  Such reserved field
   names are registered in the "Hypertext Transfer Protocol (HTTP) Field
   Name Registry" (Section 16.3.1).

### 7.6.2  Max-Forwards

   The "Max-Forwards" header field provides a mechanism with the TRACE
   (Section 9.3.8) and OPTIONS (Section 9.3.7) request methods to limit
   the number of times that the request is forwarded by proxies.  This
   can be useful when the client is attempting to trace a request that
   appears to be failing or looping mid-chain.


```abnf
     Max-Forwards = 1*DIGIT
```


   The Max-Forwards value is a decimal integer indicating the remaining
   number of times this request message can be forwarded.

   Each intermediary that receives a TRACE or OPTIONS request containing
> **MUST**: a Max-Forwards header field MUST check and update its value prior to
   forwarding the request.  If the received value is zero (0), the
> **MUST NOT**: intermediary MUST NOT forward the request; instead, the intermediary
   MUST respond as the final recipient.  If the received Max-Forwards
> **MUST**: value is greater than zero, the intermediary MUST generate an updated
   Max-Forwards field in the forwarded message with a field value that
   is the lesser of a) the received value decremented by one (1) or b)
   the recipient's maximum supported value for Max-Forwards.

> **MAY**: A recipient MAY ignore a Max-Forwards header field received with any
   other request methods.

### 7.6.3  Via

   The "Via" header field indicates the presence of intermediate
   protocols and recipients between the user agent and the server (on
   requests) or between the origin server and the client (on responses),
   similar to the "Received" header field in email (Section 3.6.7 of
   [RFC5322]).  Via can be used for tracking message forwards, avoiding
   request loops, and identifying the protocol capabilities of senders
   along the request/response chain.


```abnf
     Via = #( received-protocol RWS received-by [ RWS comment ] )

     received-protocol = [ protocol-name "/" ] protocol-version
                       ; see Section 7.8
     received-by       = pseudonym [ ":" port ]
     pseudonym         = token
```


   Each member of the Via field value represents a proxy or gateway that
   has forwarded the message.  Each intermediary appends its own
   information about how the message was received, such that the end
   result is ordered according to the sequence of forwarding recipients.

> **MUST**: A proxy MUST send an appropriate Via header field, as described
   below, in each message that it forwards.  An HTTP-to-HTTP gateway
> **MUST**: MUST send an appropriate Via header field in each inbound request
   message and MAY send a Via header field in forwarded response
   messages.

   For each intermediary, the received-protocol indicates the protocol
   and protocol version used by the upstream sender of the message.
   Hence, the Via field value records the advertised protocol
   capabilities of the request/response chain such that they remain
   visible to downstream recipients; this can be useful for determining
   what backwards-incompatible features might be safe to use in
   response, or within a later request, as described in Section 2.5.
   For brevity, the protocol-name is omitted when the received protocol
   is HTTP.

   The received-by portion is normally the host and optional port number
   of a recipient server or client that subsequently forwarded the
   message.  However, if the real host is considered to be sensitive
> **MAY**: information, a sender MAY replace it with a pseudonym.  If a port is
   not provided, a recipient MAY interpret that as meaning it was
   received on the default port, if any, for the received-protocol.

> **MAY**: A sender MAY generate comments to identify the software of each
   recipient, analogous to the User-Agent and Server header fields.
> **MAY**: However, comments in Via are optional, and a recipient MAY remove
   them prior to forwarding the message.

   For example, a request message could be sent from an HTTP/1.0 user
   agent to an internal proxy code-named "fred", which uses HTTP/1.1 to
   forward the request to a public proxy at p.example.net, which
   completes the request by forwarding it to the origin server at
   www.example.com.  The request received by www.example.com would then
   have the following Via header field:

   Via: 1.0 fred, 1.1 p.example.net

> **SHOULD**: An intermediary used as a portal through a network firewall SHOULD
   NOT forward the names and ports of hosts within the firewall region
   unless it is explicitly enabled to do so.  If not enabled, such an
> **SHOULD**: intermediary SHOULD replace each received-by host of any host behind
   the firewall by an appropriate pseudonym for that host.

> **MAY**: An intermediary MAY combine an ordered subsequence of Via header
   field list members into a single member if the entries have identical
   received-protocol values.  For example,

   Via: 1.0 ricky, 1.1 ethel, 1.1 fred, 1.0 lucy

   could be collapsed to

   Via: 1.0 ricky, 1.1 mertz, 1.0 lucy

> **SHOULD NOT**: A sender SHOULD NOT combine multiple list members unless they are all
   under the same organizational control and the hosts have already been
> **MUST NOT**: replaced by pseudonyms.  A sender MUST NOT combine members that have
   different received-protocol values.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
