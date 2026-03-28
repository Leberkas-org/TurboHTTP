---
title: "10.10.  Last-Modified"
rfc_number: 1945
rfc_section: "10.10"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 10.10: Last-Modified — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, last-modified]
---

# 10.10.  Last-Modified

## 10.10  Last-Modified

   The Last-Modified entity-header field indicates the date and time at
   which the sender believes the resource was last modified. The exact
   semantics of this field are defined in terms of how the recipient
   should interpret it:  if the recipient has a copy of this resource
   which is older than the date given by the Last-Modified field, that
   copy should be considered stale.


```abnf
       Last-Modified  = "Last-Modified" ":" HTTP-date
```


   An example of its use is

       Last-Modified: Tue, 15 Nov 1994 12:45:26 GMT

   The exact meaning of this header field depends on the implementation
   of the sender and the nature of the original resource. For files, it
   may be just the file system last-modified time. For entities with
   dynamically included parts, it may be the most recent of the set of
   last-modify times for its component parts. For database gateways, it
   may be the last-update timestamp of the record. For virtual objects,
   it may be the last time the internal state changed.

   An origin server must not send a Last-Modified date which is later
   than the server's time of message origination. In such cases, where
   the resource's last modification would indicate some time in the



   future, the server must replace that date with the message
   origination date.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
