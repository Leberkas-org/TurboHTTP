---
title: "8.  Method Definitions"
rfc_number: 1945
rfc_section: "8"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 8: Method Definitions — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, method_definitions]
---

# 8.  Method Definitions


   The set of common methods for HTTP/1.0 is defined below. Although
   this set can be expanded, additional methods cannot be assumed to
   share the same semantics for separately extended clients and servers.




## 8.1  GET

   The GET method means retrieve whatever information (in the form of an
   entity) is identified by the Request-URI. If the Request-URI refers
   to a data-producing process, it is the produced data which shall be
   returned as the entity in the response and not the source text of the
   process, unless that text happens to be the output of the process.

   The semantics of the GET method changes to a "conditional GET" if the
   request message includes an If-Modified-Since header field. A
   conditional GET method requests that the identified resource be
   transferred only if it has been modified since the date given by the
   If-Modified-Since header, as described in Section 10.9. The
   conditional GET method is intended to reduce network usage by
   allowing cached entities to be refreshed without requiring multiple
   requests or transferring unnecessary data.

## 8.2  HEAD

   The HEAD method is identical to GET except that the server must not
   return any Entity-Body in the response. The metainformation contained
   in the HTTP headers in response to a HEAD request should be identical
   to the information sent in response to a GET request. This method can
   be used for obtaining metainformation about the resource identified
   by the Request-URI without transferring the Entity-Body itself. This
   method is often used for testing hypertext links for validity,
   accessibility, and recent modification.

   There is no "conditional HEAD" request analogous to the conditional
   GET. If an If-Modified-Since header field is included with a HEAD
   request, it should be ignored.

## 8.3  POST

   The POST method is used to request that the destination server accept
   the entity enclosed in the request as a new subordinate of the
   resource identified by the Request-URI in the Request-Line. POST is
   designed to allow a uniform method to cover the following functions:

      o Annotation of existing resources;

      o Posting a message to a bulletin board, newsgroup, mailing list,
        or similar group of articles;

      o Providing a block of data, such as the result of submitting a
        form [3], to a data-handling process;

      o Extending a database through an append operation.



   The actual function performed by the POST method is determined by the
   server and is usually dependent on the Request-URI. The posted entity
   is subordinate to that URI in the same way that a file is subordinate
   to a directory containing it, a news article is subordinate to a
   newsgroup to which it is posted, or a record is subordinate to a
   database.

   A successful POST does not require that the entity be created as a
   resource on the origin server or made accessible for future
   reference. That is, the action performed by the POST method might not
   result in a resource that can be identified by a URI. In this case,
   either 200 (ok) or 204 (no content) is the appropriate response
   status, depending on whether or not the response includes an entity
   that describes the result.

   If a resource has been created on the origin server, the response
   should be 201 (created) and contain an entity (preferably of type
   "text/html") which describes the status of the request and refers to
   the new resource.

   A valid Content-Length is required on all HTTP/1.0 POST requests. An
   HTTP/1.0 server should respond with a 400 (bad request) message if it
   cannot determine the length of the request message's content.

   Applications must not cache responses to a POST request because the
   application has no way of knowing that the server would return an
   equivalent response on some future request.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
