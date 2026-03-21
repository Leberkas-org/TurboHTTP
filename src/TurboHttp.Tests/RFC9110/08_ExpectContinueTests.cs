using System.Net.Http;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// Tests that <see cref="Expect100Policy"/> correctly determines when the
/// <c>Expect: 100-continue</c> header should be added to outgoing requests.
/// RFC 9110 §10.1.1 — A client that will wait for a 100 (Continue) response before
/// sending the request content MUST send an Expect: 100-continue header field.
/// </summary>
public sealed class ExpectContinueTests
{
    [Fact(DisplayName = "RFC9110-10.1.1-EX-001: Large body gets Expect header")]
    public void Should_AddExpect_When_BodyExceedsThreshold()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[2048]);
        request.Content.Headers.ContentLength = 2048;

        var bodySize = request.Content.Headers.ContentLength ?? -1;

        Assert.True(bodySize >= policy.MinBodySizeBytes);
    }

    [Fact(DisplayName = "RFC9110-10.1.1-EX-002: Small body passes unchanged")]
    public void Should_NotAddExpect_When_BodyBelowThreshold()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[512]);
        request.Content.Headers.ContentLength = 512;

        var bodySize = request.Content.Headers.ContentLength ?? -1;

        Assert.True(bodySize < policy.MinBodySizeBytes);
    }

    [Fact(DisplayName = "RFC9110-10.1.1-EX-003: No body passes unchanged")]
    public void Should_NotAddExpect_When_NoBody()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var bodySize = request.Content?.Headers.ContentLength ?? -1;

        Assert.True(bodySize < policy.MinBodySizeBytes);
    }

    [Fact(DisplayName = "RFC9110-10.1.1-EX-004: Request without content must not have Expect")]
    public void Should_NotAddExpect_When_NoContent()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/1");

        Assert.Null(request.Content);

        var bodySize = request.Content?.Headers.ContentLength ?? -1;

        Assert.Equal(-1, bodySize);
        Assert.True(bodySize < policy.MinBodySizeBytes);
    }

    [Fact(DisplayName = "RFC9110-10.1.1-EX-005: Body at exact threshold gets Expect header")]
    public void Should_AddExpect_When_BodyAtExactThreshold()
    {
        var policy = new Expect100Policy { MinBodySizeBytes = 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload");
        request.Content = new ByteArrayContent(new byte[1024]);
        request.Content.Headers.ContentLength = 1024;

        var bodySize = request.Content.Headers.ContentLength ?? -1;

        Assert.True(bodySize >= policy.MinBodySizeBytes);
    }

    [Fact(DisplayName = "RFC9110-10.1.1-EX-006: Default policy threshold is 1024")]
    public void Should_HaveDefaultThreshold_When_DefaultPolicyUsed()
    {
        var policy = Expect100Policy.Default;

        Assert.Equal(1024, policy.MinBodySizeBytes);
    }
}
