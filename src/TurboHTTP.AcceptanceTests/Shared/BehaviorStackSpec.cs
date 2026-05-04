using Servus.Akka.TestKit;

namespace TurboHTTP.AcceptanceTests.Shared;

public sealed class BehaviorStackSpec
{
    private static BehaviorStack<int, string> Stack(string defaultValue = "default")
        => new(_ => defaultValue);

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_return_default_when_empty()
    {
        var stack = Stack("fallback");
        Assert.Equal("fallback", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_execute_pushed_behavior()
    {
        var stack = Stack();
        stack.Push(n => $"value:{n}");
        Assert.Equal("value:42", stack.Apply(42));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_execute_topmost_behavior_in_lifo_order()
    {
        var stack = Stack();
        stack.Push(_ => "first");
        stack.Push(_ => "second");
        Assert.Equal("second", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_fall_through_to_lower_behavior_after_pop()
    {
        var stack = Stack();
        stack.Push(_ => "first");
        stack.Push(_ => "second");
        stack.Pop();
        Assert.Equal("first", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_fall_through_to_default_after_all_behaviors_popped()
    {
        var stack = Stack();
        stack.Push(_ => "pushed");
        stack.Pop();
        Assert.Equal("default", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_pop_noop_when_empty()
    {
        var stack = Stack();
        stack.Pop();
        Assert.Equal("default", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_return_constant_from_PushConstant()
    {
        var stack = Stack();
        stack.PushConstant("always");
        Assert.Equal("always", stack.Apply(1));
        Assert.Equal("always", stack.Apply(2));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_throw_from_PushError()
    {
        var stack = Stack();
        var ex = new InvalidOperationException("injected");
        stack.PushError(ex);
        var thrown = Assert.Throws<InvalidOperationException>(() => stack.Apply(0));
        Assert.Same(ex, thrown);
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_auto_pop_after_PushOnce()
    {
        var stack = Stack();
        stack.PushOnce(_ => "once");
        Assert.Equal("once", stack.Apply(0));
        Assert.Equal("default", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_auto_pop_PushOnce_leaving_lower_behavior_intact()
    {
        var stack = Stack();
        stack.Push(_ => "persistent");
        stack.PushOnce(_ => "once");
        Assert.Equal("once", stack.Apply(0));
        Assert.Equal("persistent", stack.Apply(0));
        Assert.Equal("persistent", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public async Task BehaviorStack_should_block_on_PushDelayed_until_released()
    {
        var stack = Stack();
        var gate = stack.PushDelayed();

        var applyTask = Task.Run(() => stack.Apply(0));
        Assert.False(applyTask.IsCompleted);

        gate.Release("released");
        var result = await applyTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("released", result);
    }

    [Fact(Timeout = 5000)]
    public async Task BehaviorStack_should_fault_on_PushDelayed_when_gate_faulted()
    {
        var stack = Stack();
        var gate = stack.PushDelayed();

        var applyTask = Task.Run(() => stack.Apply(0));
        gate.Fault(new TimeoutException("simulated"));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            applyTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task BehaviorStack_should_fall_through_to_default_after_delayed_gate_consumed()
    {
        var stack = Stack();
        var gate = stack.PushDelayed();

        var applyTask = Task.Run(() => stack.Apply(0));
        gate.Release("delayed");
        await applyTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // PushDelayed does NOT auto-pop — caller must Pop() explicitly
        // The gate remains on the stack; applying again blocks. Test default via Pop.
        stack.Pop();
        Assert.Equal("default", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_support_nested_push_and_pop_sequence()
    {
        var stack = Stack();
        stack.Push(_ => "a");
        stack.Push(_ => "b");
        stack.Push(_ => "c");

        Assert.Equal("c", stack.Apply(0));
        stack.Pop();
        Assert.Equal("b", stack.Apply(0));
        stack.Pop();
        Assert.Equal("a", stack.Apply(0));
        stack.Pop();
        Assert.Equal("default", stack.Apply(0));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_pass_input_to_push_behavior()
    {
        var stack = new BehaviorStack<int, int>(n => n * 2);
        stack.Push(n => n + 10);
        Assert.Equal(15, stack.Apply(5));
    }

    [Fact(Timeout = 5000)]
    public void BehaviorStack_should_use_default_behavior_with_input()
    {
        var stack = new BehaviorStack<int, int>(n => n * 3);
        Assert.Equal(12, stack.Apply(4));
    }
}