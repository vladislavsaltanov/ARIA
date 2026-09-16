namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;

public sealed class RetiredFaultTests
{
    [Fact]
    public void RetiredVoiceFault_DisposesHandle()
    {
        using var h = new Harness();
        var t1 = TestShow.Track("one");
        var p = TestShow.Project("Main", TestShow.Entry(t1));
        h.Submit(new LoadShow([t1], [p], p.Id));
        h.Submit(new Play());
        var handle = h.Engine.Last!.Handle;
        h.Submit(new SetSmoothing(new Smoothing(true, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(300), TimeSpan.Zero)));

        h.Submit(new Stop());

        Assert.DoesNotContain(handle, h.Engine.Disposed);

        h.Engine.Fault(handle);

        Assert.Contains(handle, h.Engine.Disposed);
    }
}
