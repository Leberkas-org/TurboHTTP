---
title: "17.3.  Attacks Based on File and Path Names"
rfc_number: 9110
rfc_section: "17.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.3: Attacks Based on File and Path Names — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, attacks_based_on_file_and_path_names]
---

## 17.3.  Attacks Based on File and Path Names

## 17.3  Attacks Based on File and Path Names

   Origin servers frequently make use of their local file system to
   manage the mapping from target URI to resource representations.  Most
   file systems are not designed to protect against malicious file or
   path names.  Therefore, an origin server needs to avoid accessing
   names that have a special significance to the system when mapping the
   target resource to files, folders, or directories.

   For example, UNIX, Microsoft Windows, and other operating systems use
   ".." as a path component to indicate a directory level above the
   current one, and they use specially named paths or file names to send
   data to system devices.  Similar naming conventions might exist
   within other types of storage systems.  Likewise, local storage
   systems have an annoying tendency to prefer user-friendliness over
   security when handling invalid or unexpected characters,
   recomposition of decomposed characters, and case-normalization of
   case-insensitive names.

   Attacks based on such special names tend to focus on either denial-
   of-service (e.g., telling the server to read from a COM port) or
   disclosure of configuration and source files that are not meant to be
   served.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
