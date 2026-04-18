namespace TurboHTTP.Tests.Client;

/// <summary>
/// Concrete implementation for testing TurboHttpException.
/// </summary>
internal sealed class TestTurboHttpException : TurboHttpException
{
    public TestTurboHttpException(string message) : base(message)
    {
    }

    public TestTurboHttpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Concrete implementation for testing TurboProtocolException.
/// </summary>
internal sealed class TestTurboProtocolException : TurboProtocolException
{
    public TestTurboProtocolException(string message) : base(message)
    {
    }

    public TestTurboProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Concrete implementation for testing TurboTransportException.
/// </summary>
internal sealed class TestTurboTransportException(string message) : TurboTransportException(message);

public sealed class TurboHttpExceptionSpec
{
    [Fact(Timeout = 5000)]
    public void TurboHttpException_WithMessage_CreatesException()
    {
        var exception = new TestTurboHttpException("test message");

        Assert.NotNull(exception);
        Assert.Equal("test message", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpException_WithMessageAndInner_CreatesException()
    {
        var innerException = new InvalidOperationException("inner");
        var exception = new TestTurboHttpException("test message", innerException);

        Assert.NotNull(exception);
        Assert.Equal("test message", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpException_IsException()
    {
        var exception = new TestTurboHttpException("test");

        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact(Timeout = 5000)]
    public void TurboProtocolException_WithMessage_CreatesException()
    {
        var exception = new TestTurboProtocolException("protocol error");

        Assert.NotNull(exception);
        Assert.Equal("protocol error", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public void TurboProtocolException_WithMessageAndInner_CreatesException()
    {
        var innerException = new InvalidDataException("malformed");
        var exception = new TestTurboProtocolException("protocol error", innerException);

        Assert.NotNull(exception);
        Assert.Equal("protocol error", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact(Timeout = 5000)]
    public void TurboProtocolException_IsTurboHttpException()
    {
        var exception = new TestTurboProtocolException("test");

        Assert.IsAssignableFrom<TurboHttpException>(exception);
    }

    [Fact(Timeout = 5000)]
    public void TurboTransportException_WithMessage_CreatesException()
    {
        var exception = new TestTurboTransportException("connection failed");

        Assert.NotNull(exception);
        Assert.Equal("connection failed", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public void TurboTransportException_Message_IsPreserved()
    {
        var exception = new TestTurboTransportException("connection failed");

        Assert.NotNull(exception);
        Assert.Equal("connection failed", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public void TurboTransportException_IsTurboHttpException()
    {
        var exception = new TestTurboTransportException("test");

        Assert.IsAssignableFrom<TurboHttpException>(exception);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpException_CanBeCaughtAsException()
    {
        Exception? caughtException = null;

        try
        {
            throw new TestTurboHttpException("test");
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        Assert.NotNull(caughtException);
        Assert.IsType<TestTurboHttpException>(caughtException);
    }

    [Fact(Timeout = 5000)]
    public void TurboProtocolException_CanBeCaughtAsTurboHttpException()
    {
        TurboHttpException? caughtException = null;

        try
        {
            throw new TestTurboProtocolException("protocol");
        }
        catch (TurboHttpException ex)
        {
            caughtException = ex;
        }

        Assert.NotNull(caughtException);
        Assert.IsType<TestTurboProtocolException>(caughtException);
    }

    [Fact(Timeout = 5000)]
    public void MultipleExceptions_HaveIndependentStates()
    {
        var ex1 = new TestTurboHttpException("message 1");
        var ex2 = new TestTurboHttpException("message 2");

        Assert.NotEqual(ex1.Message, ex2.Message);
    }

    [Fact(Timeout = 5000)]
    public void ExceptionHierarchy_AllInheritFromTurboHttpException()
    {
        var httpEx = new TestTurboHttpException("http");
        var protocolEx = new TestTurboProtocolException("protocol");
        var transportEx = new TestTurboTransportException("transport");

        Assert.IsAssignableFrom<TurboHttpException>(httpEx);
        Assert.IsAssignableFrom<TurboHttpException>(protocolEx);
        Assert.IsAssignableFrom<TurboHttpException>(transportEx);
    }

    [Fact(Timeout = 5000)]
    public void ExceptionToString_ContainsMessage()
    {
        var exception = new TestTurboHttpException("test message");

        var exceptionString = exception.ToString();

        Assert.Contains("test message", exceptionString);
    }

    [Fact(Timeout = 5000)]
    public void ExceptionWithInnerException_ToStringContainsBoth()
    {
        var inner = new InvalidOperationException("inner message");
        var exception = new TestTurboHttpException("outer message", inner);

        var exceptionString = exception.ToString();

        Assert.Contains("outer message", exceptionString);
        Assert.Contains("inner message", exceptionString);
    }
}
