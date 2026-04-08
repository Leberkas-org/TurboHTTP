using Akka.Streams;

namespace TurboHTTP.Streams.Stages;

public static class TurboAttributes
{
    public sealed class MemoryBuffer : Attributes.IMandatoryAttribute, IEquatable<MemoryBuffer>
    {
        /// <summary>
        /// Initial encoding buffer allocation in bytes.
        /// </summary>
        public readonly int Initial;
        /// <summary>
        /// Maximum encoding buffer size in bytes.
        /// </summary>
        public readonly int Max;

        /// <summary>
        /// Configures the encoding memory buffer for a stage.
        /// </summary>
        /// <param name="initial">Initial buffer allocation in bytes.</param>
        /// <param name="max">Maximum buffer size in bytes.</param>
        public MemoryBuffer(int initial, int max)
        {
            Initial = initial;
            Max = max;
        }
        public bool Equals(MemoryBuffer? other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Initial == other.Initial && Max == other.Max;
        }

        public override bool Equals(object? obj) => obj is MemoryBuffer buffer && Equals(buffer);
        public override int GetHashCode()
        {
            unchecked
            {
                return (Initial * 397) ^ Max;
            }
        }
        public override string ToString() => $"MemoryBuffer(initial={Initial}, max={Max})";
    }

    public sealed class SubstreamQueueSize(int size) : Attributes.IMandatoryAttribute, IEquatable<SubstreamQueueSize>
    {
        public readonly int Size = size;

        public bool Equals(SubstreamQueueSize? other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return Size == other.Size;
        }

        public override bool Equals(object? obj) => obj is SubstreamQueueSize qs && Equals(qs);
        public override int GetHashCode() => Size;
        public override string ToString() => $"SubstreamQueueSize(size={Size})";
    }
}