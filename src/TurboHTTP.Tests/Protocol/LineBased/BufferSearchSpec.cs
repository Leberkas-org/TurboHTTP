using TurboHTTP.Protocol.LineBased;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class BufferSearchSpec
{
    [Fact(Timeout = 5000)]
    public void FindCrlf_should_return_index_of_cr_when_present()
    {
        var data = "header: value\r\nnext"u8.ToArray();
        Assert.Equal(13, BufferSearch.FindCrlf(data, 0));
    }

    [Fact(Timeout = 5000)]
    public void FindCrlf_should_return_negative_when_absent()
    {
        var data = "no terminator here"u8.ToArray();
        Assert.Equal(-1, BufferSearch.FindCrlf(data, 0));
    }

    [Fact(Timeout = 5000)]
    public void FindCrlf_should_skip_to_start_offset()
    {
        var data = "first\r\nsecond\r\nthird"u8.ToArray();
        Assert.Equal(13, BufferSearch.FindCrlf(data, 7));
    }

    [Fact(Timeout = 5000)]
    public void FindCrlfCrlf_should_find_double_crlf()
    {
        var data = "Host: x\r\n\r\nbody"u8.ToArray();
        Assert.Equal(7, BufferSearch.FindCrlfCrlf(data, 0));
    }

    [Fact(Timeout = 5000)]
    public void FindCrlfCrlf_should_return_negative_when_absent()
    {
        var data = "Host: x\r\nstill headers"u8.ToArray();
        Assert.Equal(-1, BufferSearch.FindCrlfCrlf(data, 0));
    }

    [Fact(Timeout = 5000)]
    public void FindSpace_should_return_index_of_first_space()
    {
        var data = "GET / HTTP/1.1"u8.ToArray();
        Assert.Equal(3, BufferSearch.FindSpace(data, 0));
    }

    [Fact(Timeout = 5000)]
    public void SkipOws_should_advance_past_spaces_and_tabs()
    {
        var data = "   \t value"u8.ToArray();
        Assert.Equal(5, BufferSearch.SkipOws(data, 0));
    }

    [Fact(Timeout = 5000)]
    public void SkipOws_should_return_start_when_no_ows()
    {
        var data = "value"u8.ToArray();
        Assert.Equal(0, BufferSearch.SkipOws(data, 0));
    }
}