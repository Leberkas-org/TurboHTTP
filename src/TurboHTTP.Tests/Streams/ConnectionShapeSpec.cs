using System.Collections.Immutable;
using Akka.Streams;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Streams;

/// <summary>
/// Tests for ConnectionShape, a custom 4-port shape for HTTP connection stages.
/// </summary>
/// <remarks>
/// Type under test: <see cref="ConnectionShape"/>.
/// Akka.Streams: Custom shapes define the ports and structure of GraphStages.
/// </remarks>
public sealed class ConnectionShapeSpec
{
    [Fact]
    public void ConnectionShape_should_initialize_with_correct_ports()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        Assert.Equal(inServer, shape.InServer);
        Assert.Equal(outResponse, shape.OutResponse);
        Assert.Equal(inApp, shape.InApp);
        Assert.Equal(outNetwork, shape.OutNetwork);
    }

    [Fact]
    public void ConnectionShape_should_report_correct_inlets()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        var inlets = shape.Inlets;
        Assert.Equal(2, inlets.Length);
        Assert.Contains(inServer, inlets);
        Assert.Contains(inApp, inlets);
    }

    [Fact]
    public void ConnectionShape_should_report_correct_outlets()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        var outlets = shape.Outlets;
        Assert.Equal(2, outlets.Length);
        Assert.Contains(outResponse, outlets);
        Assert.Contains(outNetwork, outlets);
    }

    [Fact]
    public void ConnectionShape_should_create_deep_copy()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);
        var copy = shape.DeepCopy();

        Assert.IsType<ConnectionShape>(copy);
        var copiedShape = (ConnectionShape)copy;

        Assert.NotSame(shape.InServer, copiedShape.InServer);
        Assert.NotSame(shape.OutResponse, copiedShape.OutResponse);
        Assert.NotSame(shape.InApp, copiedShape.InApp);
        Assert.NotSame(shape.OutNetwork, copiedShape.OutNetwork);

        // Port names should be preserved
        Assert.Equal(shape.InServer.Name, copiedShape.InServer.Name);
        Assert.Equal(shape.OutResponse.Name, copiedShape.OutResponse.Name);
        Assert.Equal(shape.InApp.Name, copiedShape.InApp.Name);
        Assert.Equal(shape.OutNetwork.Name, copiedShape.OutNetwork.Name);
    }

    [Fact]
    public void ConnectionShape_should_copy_from_ports()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        var newInlets = new Inlet[] { inServer.CarbonCopy(), inApp.CarbonCopy() };
        var newOutlets = new Outlet[] { outResponse.CarbonCopy(), outNetwork.CarbonCopy() };

        var copiedShape = shape.CopyFromPorts(newInlets.ToImmutableArray(), newOutlets.ToImmutableArray());

        Assert.IsType<ConnectionShape>(copiedShape);
        var result = (ConnectionShape)copiedShape;

        Assert.Equal(2, result.Inlets.Length);
        Assert.Equal(2, result.Outlets.Length);
    }

    [Fact]
    public void ConnectionShape_should_maintain_port_order_in_inlets()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        // Order should be InServer first, then InApp
        Assert.Equal(inServer, shape.Inlets[0]);
        Assert.Equal(inApp, shape.Inlets[1]);
    }

    [Fact]
    public void ConnectionShape_should_maintain_port_order_in_outlets()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        // Order should be OutResponse first, then OutNetwork
        Assert.Equal(outResponse, shape.Outlets[0]);
        Assert.Equal(outNetwork, shape.Outlets[1]);
    }

    [Fact]
    public void ConnectionShape_should_implement_shape_interface()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        Assert.IsAssignableFrom<Shape>(shape);
    }

    [Fact]
    public void ConnectionShape_deep_copy_should_create_independent_instances()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape1 = new ConnectionShape(inServer, outResponse, inApp, outNetwork);
        var shape2 = shape1.DeepCopy();
        var shape3 = shape1.DeepCopy();

        var copied2 = (ConnectionShape)shape2;
        var copied3 = (ConnectionShape)shape3;

        // Different copies should have different port instances
        Assert.NotSame(copied2.InServer, copied3.InServer);
        Assert.NotSame(copied2.OutResponse, copied3.OutResponse);
    }

    [Fact]
    public void ConnectionShape_copy_from_ports_should_preserve_port_types()
    {
        var inServer = new Inlet<IInputItem>("InServer");
        var outResponse = new Outlet<HttpResponseMessage>("OutResponse");
        var inApp = new Inlet<HttpRequestMessage>("InApp");
        var outNetwork = new Outlet<IOutputItem>("OutNetwork");

        var shape = new ConnectionShape(inServer, outResponse, inApp, outNetwork);

        var newInlets = new Inlet[] { inServer.CarbonCopy(), inApp.CarbonCopy() };
        var newOutlets = new Outlet[] { outResponse.CarbonCopy(), outNetwork.CarbonCopy() };

        var copiedShape = shape.CopyFromPorts(newInlets.ToImmutableArray(), newOutlets.ToImmutableArray());
        var result = (ConnectionShape)copiedShape;

        Assert.IsType<Inlet<IInputItem>>(result.InServer);
        Assert.IsType<Outlet<HttpResponseMessage>>(result.OutResponse);
        Assert.IsType<Inlet<HttpRequestMessage>>(result.InApp);
        Assert.IsType<Outlet<IOutputItem>>(result.OutNetwork);
    }
}
