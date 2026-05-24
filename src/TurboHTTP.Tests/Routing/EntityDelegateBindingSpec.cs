using Akka.Actor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Routing;

public sealed class EntityDelegateBindingSpec
{
    [Fact(Timeout = 5000)]
    public void OnGet_with_delegate_should_register_route()
    {
        var table = CreateApp(builder =>
        {
            builder.OnGet((string id) => new GetEntityMessage(id));
            builder.UseResolver<FakeResolver>();
        });

        var frozen = table.Freeze();
        var match = frozen.Match("GET", "/entities/42");
        Assert.True(match.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void OnPost_with_body_delegate_should_register_route()
    {
        var table = CreateApp(builder =>
        {
            builder.OnPost((string id, [FromBody] CreateEntityDto body) =>
            new CreateEntityMessage(id, body.Name));
            builder.UseResolver<FakeResolver>();
        });

        var frozen = table.Freeze();
        var match = frozen.Match("POST", "/entities/42");
        Assert.True(match.IsMatch);
    }

    private sealed record GetEntityMessage(string Id);

    private sealed record CreateEntityMessage(string Id, string Name);

    private sealed record CreateEntityDto(string Name);

    private sealed class FakeResolver : IEntityActorResolver
    {
        public ValueTask<IActorRef> ResolveAsync(IServiceProvider services, CancellationToken ct)
        {
            IActorRef? nobody = ActorRefs.Nobody;
            return ValueTask.FromResult(nobody!);
        }
    }

    private static TurboRouteTable CreateApp(Action<TurboEntityBuilder> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();
        app.MapTurboEntity("/entities/{id}", configure);
        return (app.Services.GetRequiredService<TurboRouteTable>());
    }
}
