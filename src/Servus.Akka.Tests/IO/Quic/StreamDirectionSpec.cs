using Servus.Akka.IO.Quic;

namespace Servus.Akka.Tests.IO.Quic;

public sealed class StreamDirectionSpec
{
    [Fact(Timeout = 5000)]
    public void StreamDirection_should_have_bidirectional_value()
    {
        Assert.Equal(0, (int)StreamDirection.Bidirectional);
    }

    [Fact(Timeout = 5000)]
    public void StreamDirection_should_have_write_only_value()
    {
        Assert.Equal(1, (int)StreamDirection.WriteOnly);
    }

    [Fact(Timeout = 5000)]
    public void StreamDirection_should_have_read_only_value()
    {
        Assert.Equal(2, (int)StreamDirection.ReadOnly);
    }

    [Fact(Timeout = 5000)]
    public void StreamDirection_should_have_exactly_three_values()
    {
        var values = Enum.GetValues<StreamDirection>();

        Assert.Equal(3, values.Length);
    }

    [Fact(Timeout = 5000)]
    public void StreamDirection_default_should_be_bidirectional()
    {
        var direction = default(StreamDirection);

        Assert.Equal(StreamDirection.Bidirectional, direction);
    }
}
