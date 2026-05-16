using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics;

public sealed class ReasonPhrasesSpec
{
    [Theory(Timeout = 5000)]
    [InlineData(200, "OK")]
    [InlineData(404, "Not Found")]
    [InlineData(500, "Internal Server Error")]
    [InlineData(204, "No Content")]
    [Trait("RFC", "RFC9110-15")]
    public void For_should_return_canonical_phrase_for_wellknown_code(int code, string expected)
    {
        Assert.Equal(expected, ReasonPhrases.For(code));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15")]
    public void For_should_return_empty_string_when_code_unknown()
    {
        Assert.Equal("", ReasonPhrases.For(599));
    }
}