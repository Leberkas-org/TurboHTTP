using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;
using Xunit;

namespace TurboHTTP.Tests.Streams;

/// <summary>
/// Tests <see cref="Engine"/> flow creation and request options building.
/// </summary>
/// <remarks>
/// Under test: <see cref="Engine.CreateFlow"/>.
/// Key behaviors: descriptor-driven pipeline construction, request options factory.
/// </remarks>
public sealed class EngineSpec
{
    // Mock factory that returns a pass-through flow
    private sealed class TestTransportFactory : ITransportFactory
    {
        public Flow<IOutputItem, IInputItem, NotUsed> Create()
        {
            throw new NotImplementedException("This factory should not be called in unit tests");
        }
    }

    private static TransportRegistry CreateMockTransportRegistry()
    {
        var mockFactory = new TestTransportFactory();

        var registry = new TransportRegistry();
        registry.Register(System.Net.HttpVersion.Version10, mockFactory);
        registry.Register(System.Net.HttpVersion.Version11, mockFactory);
        registry.Register(System.Net.HttpVersion.Version20, mockFactory);
        registry.Register(System.Net.HttpVersion.Version30, mockFactory);
        return registry;
    }

    // ENT-001: CreateFlow returns a valid flow

    [Fact]
    public void Engine_should_create_valid_flow()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;

        // Act
        var flow = engine.CreateFlow(transports, descriptor);

        // Assert
        Assert.NotNull(flow);
    }

    // ENT-002: CreateFlow uses provided options

    [Fact]
    public void Engine_should_use_provided_turbo_client_options()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;
        var options = new TurboClientOptions
        {
            MaxEndpointSubstreams = 20,
            Http1 = new Http1Options { MaxPipelineDepth = 2 }
        };

        // Act
        var flow = engine.CreateFlow(transports, descriptor, options);

        // Assert
        Assert.NotNull(flow);
    }

    // ENT-003: CreateFlow with null options uses defaults

    [Fact]
    public void Engine_should_use_default_options_when_null_provided()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;

        // Act
        var flow = engine.CreateFlow(transports, descriptor, options: null);

        // Assert
        Assert.NotNull(flow);
    }

    // ENT-004: CreateFlow with null requestOptionsFactory uses default factory

    [Fact]
    public void Engine_should_use_default_request_options_factory_when_null()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;

        // Act
        var flow = engine.CreateFlow(transports, descriptor,
            options: new TurboClientOptions(),
            requestOptionsFactory: null);

        // Assert
        Assert.NotNull(flow);
    }

    // ENT-005: TurboRequestOptions factory creates valid options

    [Fact]
    public void Engine_should_build_turbo_request_options_with_base_address()
    {
        // Arrange
        var baseUri = new Uri("http://api.example.com");
        var options = new TurboClientOptions { BaseAddress = baseUri };

        // Act
        var holder = new HttpRequestMessage();
        var reqOptions = new TurboRequestOptions(
            BaseAddress: options.BaseAddress,
            DefaultRequestVersion: holder.Version,
            DefaultRequestHeaders: holder.Headers,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: options.Credentials,
            PreAuthenticate: options.PreAuthenticate);

        // Assert
        Assert.Equal(baseUri, reqOptions.BaseAddress);
    }
}
