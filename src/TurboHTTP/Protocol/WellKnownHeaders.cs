using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace TurboHTTP.Protocol;

internal readonly struct WellKnownHeader : IEquatable<WellKnownHeader>
{
    public string Name { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public bool IsSensitive { get; }

    public WellKnownHeader(string name, bool isSensitive = false)
    {
        Name = name;
        Bytes = Encoding.ASCII.GetBytes(name);
        IsSensitive = isSensitive;
    }

    public WellKnownHeader(ReadOnlySpan<byte> nameBytes, bool isSensitive = false)
    {
        Name = Encoding.ASCII.GetString(nameBytes);
        Bytes = nameBytes.ToArray();
        IsSensitive = isSensitive;
    }

    public bool Equals(WellKnownHeader other) => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is WellKnownHeader other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
    public static bool operator ==(WellKnownHeader left, WellKnownHeader right) => left.Equals(right);
    public static bool operator !=(WellKnownHeader left, WellKnownHeader right) => !left.Equals(right);

    public static WellKnownHeader operator +(WellKnownHeader left, WellKnownHeader right)
        => new(left.Name + right.Name, left.IsSensitive || right.IsSensitive);

    public static WellKnownHeader operator +(WellKnownHeader left, string right)
        => new(left.Name + right, left.IsSensitive);

    public static WellKnownHeader operator +(string left, WellKnownHeader right)
        => new(left + right.Name, right.IsSensitive);

    public static implicit operator WellKnownHeader(string value) => new(value);
    public static implicit operator WellKnownHeader(byte[] value) => new(value);
    public static implicit operator WellKnownHeader(ReadOnlyMemory<byte> value) => new(value.Span);
    public static implicit operator string(WellKnownHeader header) => header.Name;
    public static implicit operator byte[](WellKnownHeader header) => header.Bytes.ToArray();
    public static implicit operator ReadOnlySpan<byte>(WellKnownHeader header) => header.Bytes.Span;
    public static implicit operator ReadOnlyMemory<byte>(WellKnownHeader header) => header.Bytes;
    public override string ToString() => Name;
}

internal static class WellKnownHeaders
{
    public static readonly WellKnownHeader Colon = new(":");
    public static readonly WellKnownHeader Comma = new(",");
    public static readonly WellKnownHeader SemiColon = new(";");
    public static readonly WellKnownHeader Space = new(" ");
    public static readonly WellKnownHeader Crlf = new("\r\n");

    // General
    public static readonly WellKnownHeader Http = new("HTTP/");
    public static readonly WellKnownHeader Http10 = Http + new WellKnownHeader("1.0");
    public static readonly WellKnownHeader Http11 = Http + new WellKnownHeader("1.1");
    public static readonly WellKnownHeader Http20 = Http + new WellKnownHeader("2");
    public static readonly WellKnownHeader Http30 = Http + new WellKnownHeader("3");
    public static readonly WellKnownHeader Host = new("Host");
    public static readonly WellKnownHeader Connection = new("Connection");
    public static readonly WellKnownHeader Upgrade = new("Upgrade");
    public static readonly WellKnownHeader Via = new("Via");

    //Method
    public static readonly WellKnownHeader Get = new("GET");
    public static readonly WellKnownHeader Put = new("PUT");
    public static readonly WellKnownHeader Post = new("POST");
    public static readonly WellKnownHeader Head = new("HEAD");
    public static readonly WellKnownHeader Patch = new("PATCH");
    public static readonly WellKnownHeader Trace = new("TRACE");
    public static readonly WellKnownHeader Delete = new("DELETE");
    public static readonly WellKnownHeader Options = new("OPTIONS");
    public static readonly WellKnownHeader Connect = new("CONNECT");

    // Content
    public static readonly WellKnownHeader ContentType = new("Content-Type");
    public static readonly WellKnownHeader ContentLength = new("Content-Length");
    public static readonly WellKnownHeader ContentEncoding = new("Content-Encoding");
    public static readonly WellKnownHeader ContentRange = new("Content-Range");
    public static readonly WellKnownHeader ContentLanguage = new("Content-Language");
    public static readonly WellKnownHeader ContentLocation = new("Content-Location");
    public static readonly WellKnownHeader ContentDisposition = new("Content-Disposition");
    public static readonly WellKnownHeader ContentMd5 = new("Content-MD5");

    // Auth (sensitive = never-index in HPACK/QPACK)
    public static readonly WellKnownHeader Authorization = new("Authorization", isSensitive: true);
    public static readonly WellKnownHeader ProxyAuthorization = new("Proxy-Authorization", isSensitive: true);
    public static readonly WellKnownHeader ProxyAuthenticate = new("Proxy-Authenticate");
    public static readonly WellKnownHeader ProxyConnection = new("Proxy-Connection");
    public static readonly WellKnownHeader Cookie = new("Cookie", isSensitive: true);
    public static readonly WellKnownHeader SetCookie = new("Set-Cookie", isSensitive: true);
    public static readonly WellKnownHeader SetCookie2 = new("Set-Cookie2");
    public static readonly WellKnownHeader WwwAuthenticate = new("WWW-Authenticate");

    // Caching
    public static readonly WellKnownHeader CacheControl = new("Cache-Control");
    public static readonly WellKnownHeader ETag = new("ETag");
    public static readonly WellKnownHeader Expires = new("Expires");
    public static readonly WellKnownHeader LastModified = new("Last-Modified");
    public static readonly WellKnownHeader IfNoneMatch = new("If-None-Match");
    public static readonly WellKnownHeader IfMatch = new("If-Match");
    public static readonly WellKnownHeader IfModifiedSince = new("If-Modified-Since");
    public static readonly WellKnownHeader IfUnmodifiedSince = new("If-Unmodified-Since");
    public static readonly WellKnownHeader IfRange = new("If-Range");
    public static readonly WellKnownHeader Pragma = new("Pragma");
    public static readonly WellKnownHeader Vary = new("Vary");
    public static readonly WellKnownHeader Age = new("Age");

    // Request
    public static readonly WellKnownHeader Accept = new("Accept");
    public static readonly WellKnownHeader AcceptEncoding = new("Accept-Encoding");
    public static readonly WellKnownHeader AcceptLanguage = new("Accept-Language");
    public static readonly WellKnownHeader AcceptCharset = new("Accept-Charset");
    public static readonly WellKnownHeader AcceptRanges = new("Accept-Ranges");
    public static readonly WellKnownHeader UserAgent = new("User-Agent");
    public static readonly WellKnownHeader Referer = new("Referer");
    public static readonly WellKnownHeader From = new("From");
    public static readonly WellKnownHeader Expect = new("Expect");
    public static readonly WellKnownHeader MaxForwards = new("Max-Forwards");
    public static readonly WellKnownHeader XForwardedFor = new("X-Forwarded-For");
    public static readonly WellKnownHeader XForwardedProto = new("X-Forwarded-Proto");
    public static readonly WellKnownHeader XRequestId = new("X-Request-Id");

    // Response
    public static readonly WellKnownHeader Server = new("Server");
    public static readonly WellKnownHeader Date = new("Date");
    public static readonly WellKnownHeader Location = new("Location");
    public static readonly WellKnownHeader RetryAfter = new("Retry-After");
    public static readonly WellKnownHeader Link = new("Link");
    public static readonly WellKnownHeader AltSvc = new("Alt-Svc");
    public static readonly WellKnownHeader StrictTransportSecurity = new("Strict-Transport-Security");
    public static readonly WellKnownHeader Warning = new("Warning");

    // Transfer
    public static readonly WellKnownHeader TransferEncoding = new("Transfer-Encoding");
    public static readonly WellKnownHeader Trailer = new("Trailer");
    public static readonly WellKnownHeader Trailers = new("Trailers");
    public static readonly WellKnownHeader Te = new("TE");

    // Security
    public static readonly WellKnownHeader Forwarded = new("Forwarded");

    // HTTP/2+3 Pseudo-Headers
    public static readonly WellKnownHeader Authority = Colon + new WellKnownHeader("authority");
    public static readonly WellKnownHeader Method = Colon + new WellKnownHeader("method");
    public static readonly WellKnownHeader Path = Colon + new WellKnownHeader("path");
    public static readonly WellKnownHeader Scheme = Colon + new WellKnownHeader("scheme");
    public static readonly WellKnownHeader Status = Colon + new WellKnownHeader("status");

    // Additional HPACK/QPACK headers
    public static readonly WellKnownHeader AccessControlAllowOrigin = new("Access-Control-Allow-Origin");
    public static readonly WellKnownHeader Allow = new("Allow");
    public static readonly WellKnownHeader Range = new("Range");
    public static readonly WellKnownHeader Refresh = new("Refresh");
    public static readonly WellKnownHeader KeepAliveHeader = new("Keep-Alive");

    // QPACK-only headers
    public static readonly WellKnownHeader AccessControlAllowHeaders = new("Access-Control-Allow-Headers");
    public static readonly WellKnownHeader AccessControlAllowCredentials = new("Access-Control-Allow-Credentials");
    public static readonly WellKnownHeader AccessControlAllowMethods = new("Access-Control-Allow-Methods");
    public static readonly WellKnownHeader AccessControlExposeHeaders = new("Access-Control-Expose-Headers");
    public static readonly WellKnownHeader AccessControlRequestHeaders = new("Access-Control-Request-Headers");
    public static readonly WellKnownHeader AccessControlRequestMethod = new("Access-Control-Request-Method");
    public static readonly WellKnownHeader ContentSecurityPolicy = new("Content-Security-Policy");
    public static readonly WellKnownHeader EarlyData = new("Early-Data");
    public static readonly WellKnownHeader ExpectCt = new("Expect-CT");
    public static readonly WellKnownHeader Origin = new("Origin");
    public static readonly WellKnownHeader Purpose = new("Purpose");
    public static readonly WellKnownHeader TimingAllowOrigin = new("Timing-Allow-Origin");
    public static readonly WellKnownHeader UpgradeInsecureRequests = new("Upgrade-Insecure-Requests");
    public static readonly WellKnownHeader XContentTypeOptions = new("X-Content-Type-Options");
    public static readonly WellKnownHeader XXssProtection = new("X-XSS-Protection");
    public static readonly WellKnownHeader XFrameOptions = new("X-Frame-Options");

    // Encodings
    public static readonly WellKnownHeader GzipValue = new("gzip");
    public static readonly WellKnownHeader DeflateValue = new("deflate");
    public static readonly WellKnownHeader BrValue = new("br");
    public static readonly WellKnownHeader CompressValue = new("compress");
    public static readonly WellKnownHeader IdentityValue = new("identity");
    public static readonly WellKnownHeader XGzipValue = new("x-gzip");

    // Connection tokens
    public static readonly WellKnownHeader CloseValue = new("close");
    public static readonly WellKnownHeader KeepAliveValue = new("keep-alive");
    public static readonly WellKnownHeader ChunkedValue = new("chunked");

    // Media types
    public static readonly WellKnownHeader ApplicationJson = new("application/json");
    public static readonly WellKnownHeader ApplicationOctetStream = new("application/octet-stream");

    // Cache directives
    public static readonly WellKnownHeader NoCache = new("no-cache");
    public static readonly WellKnownHeader NoStore = new("no-store");
    public static readonly WellKnownHeader PublicDirective = new("public");
    public static readonly WellKnownHeader PrivateDirective = new("private");
    public static readonly WellKnownHeader MaxAge300 = new("max-age=300");
    public static readonly WellKnownHeader MaxAge604800 = new("max-age=604800");

    // Misc
    public static readonly WellKnownHeader BytesValue = new("bytes");
    public static readonly WellKnownHeader TrailersValue = new("trailers");
    public static readonly WellKnownHeader TrailerValue = new("trailer");
    public static readonly WellKnownHeader NoneValue = new("none");
    private static readonly string[] StatusCodeStrings = BuildStatusCodeStrings();

    public static string GetStatusCodeString(int statusCode)
    {
        if (statusCode is >= 100 and <= 599)
        {
            return StatusCodeStrings[statusCode - 100];
        }

        return statusCode.ToString();
    }

    private static string[] BuildStatusCodeStrings()
    {
        var strings = new string[500];
        for (var i = 0; i < strings.Length; i++)
        {
            strings[i] = (i + 100).ToString();
        }

        return strings;
    }

    public static readonly WellKnownHeader ZeroValue = new("0");
    public static readonly WellKnownHeader OneValue = new("1");

    public static readonly WellKnownHeader ColonSpace = Colon + Space;
    public static readonly WellKnownHeader CommaSpace = Comma + Space;
    public static readonly WellKnownHeader SemiColonSpace = SemiColon + Space;

    public static bool TryResolve(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out string? result)
    {
        result = bytes.Length switch
        {
            1 => TryResolveLen1(bytes),
            2 => TryResolveLen2(bytes),
            3 => TryResolveLen3(bytes),
            4 => TryResolveLen4(bytes),
            5 => TryResolveLen5(bytes),
            6 => TryResolveLen6(bytes),
            7 => TryResolveLen7(bytes),
            8 => TryResolveLen8(bytes),
            9 when bytes.SequenceEqual(Forwarded) => Forwarded,
            10 => TryResolveLen10(bytes),
            11 when bytes.SequenceEqual(RetryAfter) => RetryAfter,
            11 when bytes.SequenceEqual(MaxAge300) => MaxAge300,
            12 => TryResolveLen12(bytes),
            13 => TryResolveLen13(bytes),
            14 => TryResolveLen14(bytes),
            15 => TryResolveLen15(bytes),
            16 => TryResolveLen16(bytes),
            17 => TryResolveLen17(bytes),
            18 when bytes.SequenceEqual(ProxyAuthenticate) => ProxyAuthenticate,
            19 when bytes.SequenceEqual(IfUnmodifiedSince) => IfUnmodifiedSince,
            19 when bytes.SequenceEqual(ProxyAuthorization) => ProxyAuthorization,
            19 when bytes.SequenceEqual(ContentDisposition) => ContentDisposition,
            24 when bytes.SequenceEqual(ApplicationOctetStream) => ApplicationOctetStream,
            25 when bytes.SequenceEqual(StrictTransportSecurity) => StrictTransportSecurity,
            _ => null
        };

        return result is not null;
    }

    private static string? TryResolveLen1(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(ZeroValue))
        {
            return ZeroValue;
        }

        if (b.SequenceEqual(OneValue))
        {
            return OneValue;
        }

        return null;
    }

    private static string? TryResolveLen2(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(Te))
        {
            return Te;
        }

        if (b.SequenceEqual(BrValue))
        {
            return BrValue;
        }

        return null;
    }

    private static string? TryResolveLen3(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(Age))
        {
            return Age;
        }

        if (b.SequenceEqual(Via))
        {
            return Via;
        }

        return null;
    }

    private static string? TryResolveLen4(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(Date))
        {
            return Date;
        }

        if (b.SequenceEqual(ETag))
        {
            return ETag;
        }

        if (b.SequenceEqual(Vary))
        {
            return Vary;
        }

        if (b.SequenceEqual(From))
        {
            return From;
        }

        if (b.SequenceEqual(Host))
        {
            return Host;
        }

        if (b.SequenceEqual(Link))
        {
            return Link;
        }

        if (b.SequenceEqual(GzipValue))
        {
            return GzipValue;
        }

        if (b.SequenceEqual(NoneValue))
        {
            return NoneValue;
        }

        return null;
    }

    private static string? TryResolveLen5(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(Allow))
        {
            return Allow;
        }

        if (b.SequenceEqual(Range))
        {
            return Range;
        }

        if (b.SequenceEqual(Path))
        {
            return Path;
        }

        if (b.SequenceEqual(CloseValue))
        {
            return CloseValue;
        }

        if (b.SequenceEqual(BytesValue))
        {
            return BytesValue;
        }

        return null;
    }

    private static string? TryResolveLen6(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(Accept))
        {
            return Accept;
        }

        if (b.SequenceEqual(Cookie))
        {
            return Cookie;
        }

        if (b.SequenceEqual(Expect))
        {
            return Expect;
        }

        if (b.SequenceEqual(Pragma))
        {
            return Pragma;
        }

        if (b.SequenceEqual(Server))
        {
            return Server;
        }

        if (b.SequenceEqual(Origin))
        {
            return Origin;
        }

        if (b.SequenceEqual(PublicDirective))
        {
            return PublicDirective;
        }

        return null;
    }

    private static string? TryResolveLen7(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(AltSvc))
        {
            return AltSvc;
        }

        if (b.SequenceEqual(Expires))
        {
            return Expires;
        }

        if (b.SequenceEqual(Referer))
        {
            return Referer;
        }

        if (b.SequenceEqual(Trailer))
        {
            return Trailer;
        }

        if (b.SequenceEqual(Upgrade))
        {
            return Upgrade;
        }

        if (b.SequenceEqual(Warning))
        {
            return Warning;
        }

        if (b.SequenceEqual(Method))
        {
            return Method;
        }

        if (b.SequenceEqual(Scheme))
        {
            return Scheme;
        }

        if (b.SequenceEqual(Status))
        {
            return Status;
        }

        if (b.SequenceEqual(ChunkedValue))
        {
            return ChunkedValue;
        }

        if (b.SequenceEqual(DeflateValue))
        {
            return DeflateValue;
        }

        if (b.SequenceEqual(PrivateDirective))
        {
            return PrivateDirective;
        }

        if (b.SequenceEqual(TrailerValue))
        {
            return TrailerValue;
        }

        return null;
    }

    private static string? TryResolveLen8(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(IfMatch))
        {
            return IfMatch;
        }

        if (b.SequenceEqual(IfRange))
        {
            return IfRange;
        }

        if (b.SequenceEqual(Location))
        {
            return Location;
        }

        if (b.SequenceEqual(CompressValue))
        {
            return CompressValue;
        }

        if (b.SequenceEqual(IdentityValue))
        {
            return IdentityValue;
        }

        if (b.SequenceEqual(NoCache))
        {
            return NoCache;
        }

        if (b.SequenceEqual(NoStore))
        {
            return NoStore;
        }

        if (b.SequenceEqual(TrailersValue))
        {
            return TrailersValue;
        }

        return null;
    }

    private static string? TryResolveLen10(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(Connection))
        {
            return Connection;
        }

        if (b.SequenceEqual(KeepAliveHeader))
        {
            return KeepAliveHeader;
        }

        if (b.SequenceEqual(SetCookie))
        {
            return SetCookie;
        }

        if (b.SequenceEqual(UserAgent))
        {
            return UserAgent;
        }

        if (b.SequenceEqual(Authority))
        {
            return Authority;
        }

        if (b.SequenceEqual(KeepAliveValue))
        {
            return KeepAliveValue;
        }

        return null;
    }

    private static string? TryResolveLen12(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(ContentType))
        {
            return ContentType;
        }

        if (b.SequenceEqual(MaxForwards))
        {
            return MaxForwards;
        }

        if (b.SequenceEqual(XRequestId))
        {
            return XRequestId;
        }

        return null;
    }

    private static string? TryResolveLen13(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(Authorization))
        {
            return Authorization;
        }

        if (b.SequenceEqual(CacheControl))
        {
            return CacheControl;
        }

        if (b.SequenceEqual(ContentRange))
        {
            return ContentRange;
        }

        if (b.SequenceEqual(LastModified))
        {
            return LastModified;
        }

        if (b.SequenceEqual(IfNoneMatch))
        {
            return IfNoneMatch;
        }

        return null;
    }

    private static string? TryResolveLen14(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(AcceptCharset))
        {
            return AcceptCharset;
        }

        if (b.SequenceEqual(AcceptRanges))
        {
            return AcceptRanges;
        }

        if (b.SequenceEqual(ContentLength))
        {
            return ContentLength;
        }

        if (b.SequenceEqual(MaxAge604800))
        {
            return MaxAge604800;
        }

        return null;
    }

    private static string? TryResolveLen15(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(AcceptEncoding))
        {
            return AcceptEncoding;
        }

        if (b.SequenceEqual(AcceptLanguage))
        {
            return AcceptLanguage;
        }

        if (b.SequenceEqual(XForwardedFor))
        {
            return XForwardedFor;
        }

        return null;
    }

    private static string? TryResolveLen16(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(ContentEncoding))
        {
            return ContentEncoding;
        }

        if (b.SequenceEqual(ContentLanguage))
        {
            return ContentLanguage;
        }

        if (b.SequenceEqual(ContentLocation))
        {
            return ContentLocation;
        }

        if (b.SequenceEqual(WwwAuthenticate))
        {
            return WwwAuthenticate;
        }

        if (b.SequenceEqual(ApplicationJson))
        {
            return ApplicationJson;
        }

        return null;
    }

    private static string? TryResolveLen17(ReadOnlySpan<byte> b)
    {
        if (b.SequenceEqual(IfModifiedSince))
        {
            return IfModifiedSince;
        }

        if (b.SequenceEqual(TransferEncoding))
        {
            return TransferEncoding;
        }

        if (b.SequenceEqual(XForwardedProto))
        {
            return XForwardedProto;
        }

        return null;
    }

    public static WellKnownHeader GetOrCreateHeaderName(ReadOnlySpan<byte> name)
        => TryResolve(name, out var cached) ? new WellKnownHeader(cached) : new WellKnownHeader(name);

    public static WellKnownHeader GetOrCreateHeaderValue(ReadOnlySpan<byte> value)
        => TryResolve(value, out var cached) ? new WellKnownHeader(cached) : new WellKnownHeader(value);

    public static WellKnownHeader GetOrCreateHeaderNameIgnoreCase(ReadOnlySpan<byte> name)
        => name.Length switch
        {
            0 => new WellKnownHeader(string.Empty),
            2 => EqualsIgnoreCase(name, Te) ? Te : new WellKnownHeader(name),
            3 => EqualsIgnoreCase(name, Age) ? Age :
                EqualsIgnoreCase(name, Via) ? Via : new WellKnownHeader(name),
            4 => EqualsIgnoreCase(name, Date) ? Date :
                EqualsIgnoreCase(name, ETag) ? ETag :
                EqualsIgnoreCase(name, Vary) ? Vary :
                EqualsIgnoreCase(name, From) ? From :
                EqualsIgnoreCase(name, Host) ? Host :
                EqualsIgnoreCase(name, Link) ? Link : new WellKnownHeader(name),
            5 => EqualsIgnoreCase(name, Allow) ? Allow : new WellKnownHeader(name),
            6 => EqualsIgnoreCase(name, Accept) ? Accept :
                EqualsIgnoreCase(name, Cookie) ? Cookie :
                EqualsIgnoreCase(name, Expect) ? Expect :
                EqualsIgnoreCase(name, Pragma) ? Pragma :
                EqualsIgnoreCase(name, Server) ? Server :
                new WellKnownHeader(name),
            7 => EqualsIgnoreCase(name, AltSvc) ? AltSvc :
                EqualsIgnoreCase(name, Expires) ? Expires :
                EqualsIgnoreCase(name, Referer) ? Referer :
                EqualsIgnoreCase(name, Trailer) ? Trailer :
                EqualsIgnoreCase(name, Upgrade) ? Upgrade :
                EqualsIgnoreCase(name, Warning) ? Warning :
                new WellKnownHeader(name),
            8 => EqualsIgnoreCase(name, IfMatch) ? IfMatch :
                EqualsIgnoreCase(name, IfRange) ? IfRange :
                EqualsIgnoreCase(name, Location) ? Location :
                new WellKnownHeader(name),
            9 => EqualsIgnoreCase(name, Forwarded)
                ? Forwarded
                : new WellKnownHeader(name),
            10 => EqualsIgnoreCase(name, Connection) ? Connection :
                EqualsIgnoreCase(name, KeepAliveHeader) ? KeepAliveHeader :
                EqualsIgnoreCase(name, SetCookie) ? SetCookie :
                EqualsIgnoreCase(name, UserAgent) ? UserAgent :
                new WellKnownHeader(name),
            11 => EqualsIgnoreCase(name, RetryAfter) ? RetryAfter :
                EqualsIgnoreCase(name, SetCookie2) ? SetCookie2 :
                new WellKnownHeader(name),
            12 => EqualsIgnoreCase(name, ContentType) ? ContentType :
                EqualsIgnoreCase(name, MaxForwards) ? MaxForwards :
                EqualsIgnoreCase(name, XRequestId) ? XRequestId :
                new WellKnownHeader(name),
            13 => EqualsIgnoreCase(name, Authorization) ? Authorization :
                EqualsIgnoreCase(name, CacheControl) ? CacheControl :
                EqualsIgnoreCase(name, ContentRange) ? ContentRange :
                EqualsIgnoreCase(name, LastModified) ? LastModified :
                EqualsIgnoreCase(name, IfNoneMatch) ? IfNoneMatch :
                new WellKnownHeader(name),
            14 => EqualsIgnoreCase(name, AcceptCharset) ? AcceptCharset :
                EqualsIgnoreCase(name, AcceptRanges) ? AcceptRanges :
                EqualsIgnoreCase(name, ContentLength) ? ContentLength :
                new WellKnownHeader(name),
            15 => EqualsIgnoreCase(name, AcceptEncoding) ? AcceptEncoding :
                EqualsIgnoreCase(name, AcceptLanguage) ? AcceptLanguage :
                EqualsIgnoreCase(name, XForwardedFor) ? XForwardedFor :
                new WellKnownHeader(name),
            16 => EqualsIgnoreCase(name, ContentEncoding) ? ContentEncoding :
                EqualsIgnoreCase(name, ContentLanguage) ? ContentLanguage :
                EqualsIgnoreCase(name, ContentLocation) ? ContentLocation :
                EqualsIgnoreCase(name, WwwAuthenticate) ? WwwAuthenticate :
                new WellKnownHeader(name),
            17 => EqualsIgnoreCase(name, IfModifiedSince) ? IfModifiedSince :
                EqualsIgnoreCase(name, TransferEncoding) ? TransferEncoding :
                EqualsIgnoreCase(name, XForwardedProto) ? XForwardedProto :
                new WellKnownHeader(name),
            18 => EqualsIgnoreCase(name, ProxyAuthenticate)
                ? ProxyAuthenticate
                : new WellKnownHeader(name),
            19 => EqualsIgnoreCase(name, IfUnmodifiedSince) ? IfUnmodifiedSince :
                EqualsIgnoreCase(name, ProxyAuthorization) ? ProxyAuthorization :
                new WellKnownHeader(name),
            25 => EqualsIgnoreCase(name, StrictTransportSecurity)
                ? StrictTransportSecurity
                : new WellKnownHeader(name),
            _ => new WellKnownHeader(name),
        };

    internal static bool EqualsIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if ((a[i] | 0x20) != (b[i] | 0x20))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSensitiveHeaderName(string name)
    {
        return name.Equals(Authorization, StringComparison.OrdinalIgnoreCase) ||
               name.Equals(ProxyAuthorization, StringComparison.OrdinalIgnoreCase) ||
               name.Equals(Cookie, StringComparison.OrdinalIgnoreCase) ||
               name.Equals(SetCookie, StringComparison.OrdinalIgnoreCase);
    }
}