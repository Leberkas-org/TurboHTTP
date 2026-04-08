using System.Text;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Semantics;

/// <summary>
/// Tests for <see cref="AuthChallengeParser"/> and <see cref="AuthorizationBuilder"/>.
/// RFC 9110 §11.6.1 — WWW-Authenticate challenge parsing.
/// RFC 9110 §11.2 — auth-param uniqueness and quoted-string requirements.
/// </summary>
public sealed class AuthChallengeParserSpec
{
    //  AuthChallengeParser

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_Parse_When_SingleBasicChallenge()
    {
        var challenges = AuthChallengeParser.Parse("Basic realm=\"example\"");

        Assert.Single(challenges);
        Assert.Equal("Basic", challenges[0].Scheme);
        Assert.Single(challenges[0].Parameters);
        Assert.Equal("example", challenges[0].Parameters["realm"]);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_ParseAll_When_MultipleChallengeSingleHeader()
    {
        var challenges = AuthChallengeParser.Parse(
            "Bearer realm=\"api\", scope=\"read write\", Basic realm=\"simple\"");

        Assert.Equal(2, challenges.Count);

        Assert.Equal("Bearer", challenges[0].Scheme);
        Assert.Equal("api", challenges[0].Parameters["realm"]);
        Assert.Equal("read write", challenges[0].Parameters["scope"]);

        Assert.Equal("Basic", challenges[1].Scheme);
        Assert.Equal("simple", challenges[1].Parameters["realm"]);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_Throw_When_DuplicateParameter()
    {
        var ex = Assert.Throws<FormatException>(
            () => AuthChallengeParser.Parse("Bearer realm=\"a\", realm=\"b\""));

        Assert.Contains("Duplicate parameter name", ex.Message);
        Assert.Contains("realm", ex.Message);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_Parse_When_BearerWithParams()
    {
        var challenges = AuthChallengeParser.Parse(
            "Bearer realm=\"example\", scope=\"openid profile\", error=\"invalid_token\"");

        Assert.Single(challenges);
        Assert.Equal("Bearer", challenges[0].Scheme);
        Assert.Equal(3, challenges[0].Parameters.Count);
        Assert.Equal("example", challenges[0].Parameters["realm"]);
        Assert.Equal("openid profile", challenges[0].Parameters["scope"]);
        Assert.Equal("invalid_token", challenges[0].Parameters["error"]);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_Parse_When_SchemeOnly()
    {
        var challenges = AuthChallengeParser.Parse("Negotiate");

        Assert.Single(challenges);
        Assert.Equal("Negotiate", challenges[0].Scheme);
        Assert.Empty(challenges[0].Parameters);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_MatchCaseInsensitive_When_ParameterLookup()
    {
        var challenges = AuthChallengeParser.Parse("Basic realm=\"test\"");

        Assert.Equal("test", challenges[0].Parameters["REALM"]);
        Assert.Equal("test", challenges[0].Parameters["Realm"]);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_Parse_When_QuotedStringWithEscapes()
    {
        var challenges = AuthChallengeParser.Parse("Basic realm=\"test\\\"realm\"");

        Assert.Equal("test\"realm", challenges[0].Parameters["realm"]);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_Throw_When_EmptyHeader()
    {
        Assert.Throws<ArgumentException>(() => AuthChallengeParser.Parse(""));
        Assert.Throws<ArgumentException>(() => AuthChallengeParser.Parse(null!));
        Assert.Throws<ArgumentException>(() => AuthChallengeParser.Parse("   "));
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.1")]
    public void Should_Parse_When_UnquotedParamValue()
    {
        var challenges = AuthChallengeParser.Parse("Bearer realm=example");

        Assert.Single(challenges);
        Assert.Equal("example", challenges[0].Parameters["realm"]);
    }

    //  AuthorizationBuilder

    [Fact]
    [Trait("RFC", "RFC9110-11.2.2")]
    public void Should_GenerateBase64_When_BasicAuth()
    {
        var header = AuthorizationBuilder.BuildBasic("Aladdin", "open sesame");

        Assert.StartsWith("Basic ", header);
        var base64Part = header["Basic ".Length..];
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64Part));
        Assert.Equal("Aladdin:open sesame", decoded);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.2")]
    public void Should_GenerateToken_When_BearerAuth()
    {
        var header = AuthorizationBuilder.BuildBearer("mF_9.B5f-4.1JqM");

        Assert.Equal("Bearer mF_9.B5f-4.1JqM", header);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.2")]
    public void Should_UseQuotedString_When_CustomParams()
    {
        var parameters = new Dictionary<string, string>
        {
            ["realm"] = "example",
            ["nonce"] = "abc123"
        };

        var header = AuthorizationBuilder.BuildCustom("Digest", parameters);

        Assert.StartsWith("Digest ", header);
        Assert.Contains("realm=\"example\"", header);
        Assert.Contains("nonce=\"abc123\"", header);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.2")]
    public void Should_EscapeQuotes_When_ValueContainsQuote()
    {
        var parameters = new Dictionary<string, string>
        {
            ["realm"] = "say \"hello\""
        };

        var header = AuthorizationBuilder.BuildCustom("Custom", parameters);

        Assert.Contains("realm=\"say \\\"hello\\\"\"", header);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.2")]
    public void Should_GenerateBase64_When_EmptyPassword()
    {
        var header = AuthorizationBuilder.BuildBasic("user", "");

        var base64Part = header["Basic ".Length..];
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64Part));
        Assert.Equal("user:", decoded);
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.2")]
    public void Should_Throw_When_EmptyBearerToken()
    {
        Assert.Throws<ArgumentException>(() => AuthorizationBuilder.BuildBearer(""));
        Assert.Throws<ArgumentException>(() => AuthorizationBuilder.BuildBearer("   "));
    }

    [Fact]
    [Trait("RFC", "RFC9110-11.2.2")]
    public void Should_ReturnSchemeOnly_When_NoCustomParams()
    {
        var header = AuthorizationBuilder.BuildCustom("Negotiate", new Dictionary<string, string>());

        Assert.Equal("Negotiate", header);
    }
}
