using TurboHTTP.Server.Entity;

namespace TurboHTTP.Tests.Server.Entity;

public sealed class EntityResponseMapperCollectionSpec
{
    private record OrderResult(string Id);

    private sealed record DerivedResult(string Id) : OrderResult(Id);

    [Fact(Timeout = 5000)]
    public async Task FindMapper_should_return_exact_type_match()
    {
        var collection = new EntityResponseMapperCollection();
        var invoked = false;
        collection.Add<OrderResult>((_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        var mapper = collection.FindMapper(typeof(OrderResult));
        Assert.NotNull(mapper);
        await mapper(null!, new OrderResult("1"));
        Assert.True(invoked);
    }

    [Fact(Timeout = 5000)]
    public void FindMapper_should_return_null_for_unregistered_type()
    {
        var collection = new EntityResponseMapperCollection();
        collection.Add<OrderResult>((_, _) => Task.CompletedTask);

        var mapper = collection.FindMapper(typeof(string));
        Assert.Null(mapper);
    }

    [Fact(Timeout = 5000)]
    public async Task FindMapper_should_fall_back_to_assignable_match()
    {
        var collection = new EntityResponseMapperCollection();
        var capturedId = "";
        collection.Add<OrderResult>((_, r) =>
        {
            capturedId = r.Id;
            return Task.CompletedTask;
        });

        var mapper = collection.FindMapper(typeof(DerivedResult));
        Assert.NotNull(mapper);
        await mapper(null!, new DerivedResult("derived-1"));
        Assert.Equal("derived-1", capturedId);
    }

    [Fact(Timeout = 5000)]
    public async Task FindMapper_should_prefer_exact_over_assignable()
    {
        var collection = new EntityResponseMapperCollection();
        var matched = "";
        collection.Add<OrderResult>((_, _) =>
        {
            matched = "base";
            return Task.CompletedTask;
        });
        collection.Add<DerivedResult>((_, _) =>
        {
            matched = "derived";
            return Task.CompletedTask;
        });

        var mapper = collection.FindMapper(typeof(DerivedResult));
        Assert.NotNull(mapper);
        await mapper(null!, new DerivedResult("x"));
        Assert.Equal("derived", matched);
    }
}