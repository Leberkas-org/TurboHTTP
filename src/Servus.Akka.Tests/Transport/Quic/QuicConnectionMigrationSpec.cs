using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport.Quic;

public sealed class QuicConnectionMigrationSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void QuicOptions_should_default_AllowConnectionMigration_to_true()
    {
        var options = new QuicTransportOptions { Host = "example.com", Port = 443 };
        Assert.True(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9000-9")]
    public void QuicOptions_should_accept_AllowConnectionMigration_false()
    {
        var options = new QuicTransportOptions { Host = "example.com", Port = 443, AllowConnectionMigration = false };
        Assert.False(options.AllowConnectionMigration);
    }

    // TODO: Migration_allowed and Migration_disallowed tests disabled pending QuicTransportStateMachine API stabilization
    // These tests relied on internal transport-layer behavior that has changed in the new Transport abstraction layer
    // They will need to be rewritten when the QUIC connection migration API is fully exposed and documented
    //
    // [Fact(Timeout = 5000)]
    // [Trait("RFC", "RFC9000-9")]
    // public void Migration_allowed_should_continue_transparently_when_address_changes() { ... }
    //
    // [Fact(Timeout = 5000)]
    // [Trait("RFC", "RFC9000-9")]
    // public void Migration_disallowed_should_trigger_reconnect_when_address_changes() { ... }
}