# Content Encoding

TurboHTTP automatically decompresses compressed HTTP responses. When a server sends a `Content-Encoding` header, TurboHTTP decompresses the body transparently before returning the response — the calling code always receives plain, uncompressed content.

## Supported Encodings

| Encoding | Header token     | Notes                                                  |
| -------- | ---------------- | ------------------------------------------------------ |
| Gzip     | `gzip`, `x-gzip` | Most common; used by the majority of web servers       |
| Deflate  | `deflate`        | Handles both zlib-wrapped and raw deflate formats      |
| Brotli   | `br`             | Best compression ratio; requires modern server support |
| Identity | `identity`       | No compression; body passed through unchanged          |

## How It Works

For HTTP/1.1 requests, TurboHTTP automatically adds `Accept-Encoding: gzip, deflate, br` to every outgoing request unless you have already set an `Accept-Encoding` header yourself. This tells the server which encodings the client can handle.

When a response arrives:

1. TurboHTTP reads the `Content-Encoding` header.
2. The body is decompressed using the appropriate algorithm.
3. The `Content-Encoding` header is removed from the response.
4. `Content-Length` is updated to reflect the decompressed size.

The final `HttpResponseMessage` you receive has an uncompressed body and accurate content headers.

```csharp
var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/data"));

// Body is already decompressed — no manual handling needed
var text = await response.Content.ReadAsStringAsync();
```

## Stacked Encodings

If a response uses multiple encodings (e.g., `Content-Encoding: gzip, br`), TurboHTTP decodes them in the correct reverse order — the outermost encoding is decoded first. This matches how stacked encodings are applied by the server.

## Unknown Encodings

If the server sends a `Content-Encoding` value that TurboHTTP does not recognise, it throws `HttpDecoderException` rather than silently returning corrupted data. This prevents your application from processing a response body it cannot correctly interpret.

## Overriding Accept-Encoding

To request a specific encoding (or no compression at all), set `Accept-Encoding` on the request before sending:

```csharp
// Request no compression
var request = new HttpRequestMessage(HttpMethod.Get, "/data");
request.Headers.AcceptEncoding.ParseAdd("identity");

var response = await client.SendAsync(request);
```

```csharp
// Request only gzip
var request = new HttpRequestMessage(HttpMethod.Get, "/data");
request.Headers.AcceptEncoding.ParseAdd("gzip");

var response = await client.SendAsync(request);
```

When `AcceptEncoding` is already set on the request, TurboHTTP skips its automatic `Accept-Encoding` injection and uses your value instead.

## HTTP/2 Requests

For HTTP/2 requests, TurboHTTP does not automatically inject `Accept-Encoding`. If you want compressed responses over HTTP/2, add the header explicitly:

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "/data");
request.Version = HttpVersion.Version20;
request.Headers.AcceptEncoding.ParseAdd("gzip, br");

var response = await client.SendAsync(request);
// Body is decompressed automatically if the server compresses it
```

Decompression itself works the same regardless of protocol version — if the server includes a `Content-Encoding` header, TurboHTTP decodes the body.
