using Servus.Akka.IO;

namespace Servus.Akka.Tests.IO;

public sealed class AbruptCloseExceptionSpec
{
    [Fact(Timeout = 5000)]
    public void AbruptCloseException_should_have_expected_message()
    {
        var ex = new AbruptCloseException();

        Assert.Equal("Connection closed abruptly without close_notify", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void AbruptCloseException_should_derive_from_exception()
    {
        var ex = new AbruptCloseException();

        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact(Timeout = 5000)]
    public void AbruptCloseException_should_have_null_inner_exception()
    {
        var ex = new AbruptCloseException();

        Assert.Null(ex.InnerException);
    }
}
