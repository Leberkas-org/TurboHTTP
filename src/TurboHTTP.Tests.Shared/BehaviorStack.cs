namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Composable behavior stack for test error/delay injection.
/// Behaviors are applied LIFO — the most recently pushed behavior handles the next Apply call.
/// Not thread-safe; designed for single-threaded Akka stage execution.
/// </summary>
public sealed class BehaviorStack<TIn, TOut>
{
    private readonly Func<TIn, TOut> _default;
    private readonly Stack<Func<TIn, TOut>> _stack = new();

    public BehaviorStack(Func<TIn, TOut> defaultBehavior)
    {
        _default = defaultBehavior;
    }

    /// <summary>Pushes a behavior on top of the stack.</summary>
    public void Push(Func<TIn, TOut> behavior) => _stack.Push(behavior);

    /// <summary>Pushes a behavior that always returns the same constant value.</summary>
    public void PushConstant(TOut value) => Push(_ => value);

    /// <summary>Pushes a behavior that throws the given exception when applied.</summary>
    public void PushError(Exception exception) => Push(_ => throw exception);

    /// <summary>
    /// Pushes a delayed behavior and returns a gate.
    /// Apply blocks the calling thread until <see cref="DelayGate{TIn,TOut}.Release"/> or
    /// <see cref="DelayGate{TIn,TOut}.Fault"/> is called from another thread.
    /// </summary>
    public DelayGate<TIn, TOut> PushDelayed()
    {
        var gate = new DelayGate<TIn, TOut>();
        Push(gate.Execute);
        return gate;
    }

    /// <summary>
    /// Pushes a one-shot behavior that automatically pops itself after the first invocation.
    /// </summary>
    public void PushOnce(Func<TIn, TOut> behavior)
    {
        Push(input =>
        {
            Pop();
            return behavior(input);
        });
    }

    /// <summary>Removes the topmost behavior. No-op if the stack is empty.</summary>
    public void Pop() => _stack.TryPop(out _);

    /// <summary>
    /// Executes the topmost behavior. Falls through to the default behavior if the stack is empty.
    /// </summary>
    public TOut Apply(TIn input)
    {
        if (_stack.TryPeek(out var behavior))
        {
            return behavior(input);
        }

        return _default(input);
    }
}

/// <summary>
/// Gate returned by <see cref="BehaviorStack{TIn,TOut}.PushDelayed"/>.
/// Blocks the Apply call until Released or Faulted.
/// </summary>
public sealed class DelayGate<TIn, TOut>
{
    private readonly TaskCompletionSource<TOut> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TOut Execute(TIn _) => _tcs.Task.GetAwaiter().GetResult();

    /// <summary>Unblocks the pending Apply call and returns <paramref name="value"/>.</summary>
    public void Release(TOut value) => _tcs.TrySetResult(value);

    /// <summary>Unblocks the pending Apply call and causes it to throw <paramref name="exception"/>.</summary>
    public void Fault(Exception exception) => _tcs.TrySetException(exception);
}
