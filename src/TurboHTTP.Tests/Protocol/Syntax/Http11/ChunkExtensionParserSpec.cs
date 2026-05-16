using TurboHTTP.Protocol.Syntax.Http11;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11;

[Trait("RFC", "RFC9112")]
public sealed class ChunkExtensionParserSpec
{
    [Fact(Timeout = 5000)]
    public void EmptyExtensions_ShouldParse()
    {
        var result = ChunkExtensionParser.TryParse([]);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void NameOnlyExtension_ShouldParse()
    {
        var bytes = "name"u8.ToArray();
        var result = ChunkExtensionParser.TryParse(bytes);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void NameValueExtension_ShouldParse()
    {
        var bytes = "name=value"u8.ToArray();
        var result = ChunkExtensionParser.TryParse(bytes);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void QuotedStringValue_ShouldParse()
    {
        var bytes = "name=\"quoted value\""u8.ToArray();
        var result = ChunkExtensionParser.TryParse(bytes);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void MissingSemicolon_ShouldFail()
    {
        // Multiple extensions without semicolon separator should fail
        var bytes = "name1 name2"u8.ToArray();
        var result = ChunkExtensionParser.TryParse(bytes);

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public void MultipleValidExtensions_ShouldParse()
    {
        var bytes = "name1=value1;name2=value2"u8.ToArray();
        var result = ChunkExtensionParser.TryParse(bytes);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void ExtensionWithWhitespace_ShouldParse()
    {
        var bytes = "name = value "u8.ToArray();
        var result = ChunkExtensionParser.TryParse(bytes);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void EscapedCharacterInQuotedString_ShouldParse()
    {
        var bytes = "name=\"value\\\"with\\\"quotes\""u8.ToArray();
        var result = ChunkExtensionParser.TryParse(bytes);

        Assert.True(result);
    }
}