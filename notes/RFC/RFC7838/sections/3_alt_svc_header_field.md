---
rfc_section: '3'
---
# RFC7838 §3 – The Alt-Svc HTTP Header Field

## Overview

The Alt-Svc header field advertises the availability of alternative services to HTTP clients. It specifies one or more alternative protocols and endpoints available for the origin.

## Syntax

```
Alt-Svc = clear / alt-svc-field-value
clear = DQUOTE DQUOTE
alt-svc-field-value = alt-svc *( OWS "," OWS alt-svc )
alt-svc = protocol-id "=" alt-authority *( OWS ";" OWS alt-svc-param )
protocol-id = token
alt-authority = host ":" port / host
alt-svc-param = token [ "=" ( token / quoted-string ) ]
```

### Protocol-id

The protocol-id identifies the alternative protocol. Common values:
- `h2` – HTTP/2
- `h3` – HTTP/3 (QUIC)
- `h2c` – HTTP/2 Cleartext (non-TLS)

### Alt-Authority

The alt-authority specifies the host and port where the alternative service is available.

**Examples:**
- `Alt-Svc: h2=":443"` – Same host, alternative protocol on port 443
- `Alt-Svc: h3="example.com:443"` – Different host and port
- `Alt-Svc: h2="cdn.example.com"` – Different host, standard port 443 (TLS assumed)

## Parameters

### ma (Max-Age)

The `ma` parameter specifies the freshness lifetime in seconds (default: delta-seconds).

```
ma=<delta-seconds>
```

- Controls how long clients should cache the Alt-Svc information
- If omitted, the value SHOULD be treated as: 24 hours (86400 seconds)
- Clients MAY cache indefinitely if the alternative service is accessed via HTTPS

**Example:**
```
Alt-Svc: h2=":443"; ma=3600
```

### persist

The `persist` parameter indicates whether the Alt-Svc information should persist across browser restarts (optional parameter, value `1` or not specified = true).

```
persist=1
```

- Intended for HTTPS origins only
- Clients MAY ignore this parameter for non-HTTPS origins
- Allows persistent caching across client restarts

**Example:**
```
Alt-Svc: h3="example.com:443"; ma=31536000; persist=1
```

## Clear Value

A clear Alt-Svc header (empty quoted string) signals clients to clear any cached Alt-Svc information:

```
Alt-Svc: clear
```

or

```
Alt-Svc: ""
```

Clients MUST treat this as: "Remove all cached alternative services for this origin."

## Multiple Values

Multiple alternative services can be advertised in a single header:

```
Alt-Svc: h2=":443", h3=":443"; ma=3600; persist=1
```

Or in separate header instances.

## Port Omission

If the port is omitted in alt-authority:
- Assume port **443** (standard HTTPS) for TLS-based origins
- Assume port **80** for non-TLS origins (h2c, http)

## Header Field Processing

- Alt-Svc header MAY be sent in response to any request method
- MUST NOT be sent in HTTP/1.0 responses (not applicable)
- Is treated as "hop-by-hop" in HTTP/1.1; proxies MUST NOT forward it upstream
- In HTTP/2, sent as a response header (or ALTSVC frame)

## Examples

### HTTP/1.1 Example
```
HTTP/1.1 200 OK
Alt-Svc: h2=":443"; ma=3600
Alt-Svc: h3=":443"; ma=3600; persist=1

...
```

### HTTP/2 Example (response)
```
:status: 200
alt-svc: h3=":443"; ma=7200; persist=1
...
```

## Validation Rules

- Protocol-id is case-insensitive token
- Alt-authority MUST be a valid host:port combination
- ma value MUST be a non-negative integer
- persist parameter SHOULD only appear in HTTPS responses
- Clients MUST ignore unknown parameters
- Clients MUST ignore Alt-Svc headers with invalid syntax
