using System.Net;
using System.Net.Http.Headers;
using TurboHttp.Protocol.Semantics;

namespace TurboHttp.Tests.Semantics;

/// <summary>
/// Tests 206 Partial Content response validation per RFC 9110 §15.3.7.
/// A 206 response MUST contain a Content-Range header (single part) or use
/// multipart/byteranges content type (multiple parts). Non-206 responses skip validation.
/// </summary>
/// <remarks>
/// Class under test: <see cref="PartialContentValidator"/>.
/// </remarks>
public sealed class PartialContentSpec
{
    [Fact]
    [Trait("RFC", "RFC9110-15.3.7")]
    public void Should_BeValid_When_ContentRangePresent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[100]);
        response.Content.Headers.Add("Content-Range", "bytes 0-99/200");

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.False(result.IsMultipartByteRanges);
        Assert.False(result.Skipped);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.3.7")]
    public void Should_BeInvalid_When_NoContentRange()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[100]);

        var result = PartialContentValidator.Validate(response);

        Assert.False(result.IsValid);
        Assert.False(result.Skipped);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Content-Range", result.ErrorMessage);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.3.7")]
    public void Should_Detect_When_MultipartByteranges()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[200]);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/byteranges")
        {
            Parameters = { new NameValueHeaderValue("boundary", "\"example-boundary\"") }
        };

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.IsMultipartByteRanges);
        Assert.False(result.Skipped);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.3.7")]
    public void Should_Skip_When_Not206()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent(new byte[100]);

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.Skipped);
        Assert.False(result.IsMultipartByteRanges);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.3.7")]
    public void Should_Skip_When_304NotModified()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotModified);
        response.Content = new ByteArrayContent([]);

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.Skipped);
    }

    [Fact]
    [Trait("RFC", "RFC9110-15.3.7")]
    public void Should_DetectMultipart_When_BothContentRangeAndMultipart()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[200]);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/byteranges")
        {
            Parameters = { new NameValueHeaderValue("boundary", "\"sep\"") }
        };
        response.Content.Headers.Add("Content-Range", "bytes 0-99/200");

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.IsMultipartByteRanges);
    }
}
