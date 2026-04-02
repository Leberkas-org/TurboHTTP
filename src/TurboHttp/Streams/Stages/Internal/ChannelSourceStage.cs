using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages.Internal;

/// <summary>
/// A <see cref="GraphStage{TShape}"/> backed by a <see cref="Channel{T}"/> that delivers
/// elements downstream on demand. Used by <see cref="GroupByRequestEndpointStage{T}"/> as a
/// per-slot substream source, replacing <c>Source.Queue</c>.
/// <para>
/// The key advantage over <c>Source.Queue</c> is that the producer (GroupBy stage) writes items
/// via a synchronous <see cref="System.Threading.Channels.ChannelWriter{T}.TryWrite"/> call with no
/// Akka actor Ask round-trip. The consumer (this stage) signals capacity via an
/// <paramref name="onConsumed"/> callback after each item is pushed downstream, allowing the
/// producer to drain its local pending queue.
/// </para>
/// </summary>
/// <typeparam name="T">The element type flowing through the stage.</typeparam>
internal sealed class ChannelSourceStage<T> : GraphStage<SourceShape<T>>
{
    private readonly Channel<T> _channel;
    private readonly Action? _onConsumed;
    private readonly TaskCompletionSource _completionTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Outlet<T> _out = new("ChannelSource.Out");

    /// <summary>
    /// Synchronous writer; <see cref="GroupByRequestEndpointStage{T}"/> calls
    /// <see cref="System.Threading.Channels.ChannelWriter{T}.TryWrite"/> to offer items
    /// without an actor round-trip.
    /// </summary>
    internal ChannelWriter<T> Writer => _channel.Writer;

    /// <summary>
    /// Completes when this stage terminates — used by
    /// <see cref="GroupByRequestEndpointStage{T}"/> as the dead-slot sentinel
    /// (replaces <c>ISourceQueueWithComplete.WatchCompletionAsync()</c>).
    /// </summary>
    internal Task Completion => _completionTcs.Task;

    /// <summary>
    /// Approximate number of items currently buffered in the channel.
    /// Zero means the channel is empty and a new item can be written immediately.
    /// </summary>
    internal int Count => _channel.Reader.Count;

    public ChannelSourceStage(int capacity, Action? onConsumed = null)
    {
        _onConsumed = onConsumed;
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        Shape = new SourceShape<T>(_out);
    }

    public override SourceShape<T> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    /// <summary>
    /// Drains all items currently buffered in the channel without consuming them downstream.
    /// Called by <see cref="GroupByRequestEndpointStage{T}"/> during dead-slot recovery so that
    /// buffered-but-undelivered items can be re-routed to an alive slot.
    /// </summary>
    internal IReadOnlyList<T> DrainRemaining()
    {
        var items = new List<T>();
        while (_channel.Reader.TryRead(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly ChannelSourceStage<T> _stage;
        private Action<T>? _onItemCallback;
        private Action? _onChannelDoneCallback;
        private bool _waiting;

        public Logic(ChannelSourceStage<T> stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._out,
                onPull: TryPush,
                onDownstreamFinish: _ =>
                {
                    stage.Writer.TryComplete();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            _onItemCallback = GetAsyncCallback<T>(item =>
            {
                _waiting = false;
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, item);
                    _stage._onConsumed?.Invoke();
                }
            });

            _onChannelDoneCallback = GetAsyncCallback(() => CompleteStage());
        }

        public override void PostStop()
        {
            _stage.Writer.TryComplete();
            _stage._completionTcs.TrySetResult();
        }

        private void TryPush()
        {
            if (_stage._channel.Reader.TryRead(out var item))
            {
                Push(_stage._out, item);
                _stage._onConsumed?.Invoke();
                return;
            }

            if (!_waiting)
            {
                _waiting = true;
                ScheduleWait();
            }
        }

        private void ScheduleWait()
        {
            var vt = _stage._channel.Reader.WaitToReadAsync();

            // Fast synchronous path — item was already available when we checked.
            if (vt.IsCompleted)
            {
                _waiting = false;
                if (vt.IsCompletedSuccessfully && vt.Result &&
                    _stage._channel.Reader.TryRead(out var item))
                {
                    Push(_stage._out, item);
                    _stage._onConsumed?.Invoke();
                }
                else if (!vt.IsCompletedSuccessfully || !vt.Result)
                {
                    // Channel completed with no more items.
                    _onChannelDoneCallback!();
                }
                return;
            }

            // Slow async path — wait for an item or channel completion.
            // Use awaiter callback instead of .AsTask() to avoid the Task allocation.
            var itemCb = _onItemCallback!;
            var doneCb = _onChannelDoneCallback!;
            var reader = _stage._channel.Reader;

            vt.GetAwaiter().OnCompleted(() =>
            {
                bool hasMore;
                try
                {
                    hasMore = vt.Result;
                }
                catch
                {
                    doneCb();
                    return;
                }

                if (hasMore && reader.TryRead(out var item))
                {
                    itemCb(item);
                }
                else
                {
                    doneCb();
                }
            });
        }
    }
}
