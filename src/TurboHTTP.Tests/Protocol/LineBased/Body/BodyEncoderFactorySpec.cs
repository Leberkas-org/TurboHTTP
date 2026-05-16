using System.Net;
using TurboHTTP.Protocol.LineBased.Body;

namespace TurboHTTP.Tests.Protocol.LineBased.Body;

public sealed class BodyEncoderFactorySpec
{
    private sealed class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_null_for_null_content()
    {
        var encoder = BodyEncoderFactory.Create(null, HttpVersion.Version11);
        Assert.Null(encoder);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_streamed_for_http11_known_length()
    {
        var content = new ByteArrayContent(new byte[100]);
        var encoder = BodyEncoderFactory.Create(content, HttpVersion.Version11);
        Assert.IsType<ContentLengthStreamedBodyEncoder>(encoder);
        encoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_chunked_and_set_header_for_http11_unknown_length()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/");
        var content = new StreamContent(new NonSeekableStream());
        request.Content = content;

        var encoder = BodyEncoderFactory.Create(content, HttpVersion.Version11, request.Headers);

        Assert.IsType<ChunkedBodyEncoder>(encoder);
        Assert.True(request.Headers.TransferEncodingChunked);
        encoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_buffered_for_http10_known_length()
    {
        var content = new ByteArrayContent(new byte[200_000]);
        var encoder = BodyEncoderFactory.Create(content, HttpVersion.Version10);
        Assert.IsType<ContentLengthBufferedBodyEncoder>(encoder);
        encoder.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_buffered_for_http10_unknown_length()
    {
        var content = new StreamContent(new MemoryStream(new byte[100]));
        var encoder = BodyEncoderFactory.Create(content, HttpVersion.Version10);
        Assert.IsType<ContentLengthBufferedBodyEncoder>(encoder);
        encoder.Dispose();
    }
}