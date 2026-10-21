namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class RemainingTextTests
{
    [Fact]
    public void NoTrack_ShowsBarePlaceholderWithoutMinus()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus, new PlaybackMonitor());

        Assert.Equal("--:--", vm.RemainingText);
    }

    [Fact]
    public void PlayingTrack_ShowsCountdownWithMinus_AndResetsOnUnbind()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var monitor = new PlaybackMonitor();
        using var vm = new TransportViewModel(bus, monitor);
        var deck = new DeckContent(null, TrackId.New(), "test", null, EndAction.Pause, TimeSpan.FromSeconds(90), TimeSpan.Zero);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        monitor.Bind(new StreamHandle(1), deck);
        monitor.Publish(new StreamHandle(1), TimeSpan.FromSeconds(30));
        Assert.Equal("−01:00", vm.RemainingText);
        Assert.Contains(nameof(TransportViewModel.RemainingText), changed);

        monitor.Unbind(new StreamHandle(1));
        Assert.Equal("--:--", vm.RemainingText);
    }
}
