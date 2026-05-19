using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class RegistrationValidationSpec
{
    [Fact(Timeout = 5000)]
    public void Bind_should_reject_multiple_FromBody_parameters()
    {
        Delegate handler = ([FromBody] CreateDto a, [FromBody] UpdateDto b) => TypedResults.Ok();
        var ex = Assert.Throws<InvalidOperationException>(() => DelegateHandlerBinder.Bind("/test", handler));
        Assert.Contains("FromBody", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Bind_should_accept_single_FromBody_parameter()
    {
        Delegate handler = ([FromBody] CreateDto body) => TypedResults.Ok();
        var bound = DelegateHandlerBinder.Bind("/items", handler);
        Assert.NotNull(bound);
    }

    public sealed record CreateDto(string Name);
    public sealed record UpdateDto(string Name);
}
