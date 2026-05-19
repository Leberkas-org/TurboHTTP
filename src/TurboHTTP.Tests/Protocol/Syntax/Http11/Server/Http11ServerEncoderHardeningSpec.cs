using System.Text;
using Akka.Actor;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerEncoderHardeningSpec
{
    private static Http11ServerEncoder MakeEncoder(bool withDate = false) =>
        new(Http11ServerEncoderOptions.Default with { WriteDateHeader = withDate });

    [Theory(Timeout = 5000)]
    [InlineData("Connection")]
    [InlineData("Keep-Alive")]
    [InlineData("Transfer-Encoding")]
    [InlineData("TE")]
    [InlineData("Upgrade")]
    [InlineData("Proxy-Authenticate")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Trailer")]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void Encode_should_strip_hop_by_hop_header(string headerName)
    {
        var encoder = MakeEncoder();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };

        var headerAdded = response.Headers.TryAddWithoutValidation(headerName, "test-value");

        if (!headerAdded)
        {
            response.Content.Headers.TryAddWithoutValidation(headerName, "test-value");
        }

        var buffer = new byte[4096];
        var written = encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.DoesNotContain($"{headerName}:", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void Encode_should_add_connection_close_when_requested()
    {
        var encoder = MakeEncoder();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false, connectionClose: true);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Connection: close", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Encode_should_not_add_content_length_when_chunked()
    {
        var encoder = MakeEncoder();
        var body = "test body"u8.ToArray();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: true);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.DoesNotContain("Content-Length:", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.1")]
    public void Encode_should_not_duplicate_existing_date_header()
    {
        var encoder = MakeEncoder(withDate: true);
        var existingDate = "Mon, 17 May 2021 12:00:00 GMT";
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        response.Headers.Date = DateTimeOffset.Parse(existingDate);
        var buffer = new byte[4096];

        var written = encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        var dateCount = result.Split("Date:").Length - 1;
        Assert.Equal(1, dateCount);
    }
}
