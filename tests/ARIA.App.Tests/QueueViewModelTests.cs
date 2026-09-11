namespace Aria.App.Tests;

using System.Collections.Immutable;
using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class QueueViewModelTests
{
    private static readonly Track TestTrack = new(
        TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    private static CommandBus LoadedBus()
    {
        var entry = new PlaylistEntry(EntryId.New(), TestTrack.Id);
        var playlist = new Playlist(PlaylistId.New(), "Main", [entry]);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [playlist], playlist.Id));
        return bus;
    }

    [Fact]
    public void Rebuilds_FromQueueDelta()
    {
        var bus = LoadedBus();
        bus.Submit(new ClientId("setup"), 2, new EnqueueTrack(TestTrack.Id));
        bus.Submit(new ClientId("setup"), 3, new EnqueueTrack(TestTrack.Id));
        using var vm = new QueueViewModel(bus);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("test", vm.Items[0].DisplayName);
    }

    [Fact]
    public void LiveDelta_AppendsItems()
    {
        var bus = LoadedBus();
        using var vm = new QueueViewModel(bus);
        bus.Submit(new ClientId("setup"), 2, new EnqueueTrack(TestTrack.Id));
        bus.Submit(new ClientId("setup"), 3, new EnqueueTrack(TestTrack.Id));

        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void ClearQueue_RemovesAll()
    {
        var bus = LoadedBus();
        bus.Submit(new ClientId("setup"), 2, new EnqueueTrack(TestTrack.Id));
        using var vm = new QueueViewModel(bus);

        vm.ClearQueueCommand.Execute(null);

        Assert.Empty(vm.Items);
    }

    [Fact]
    public void IsCurrent_MarkedForRemainingDuplicate()
    {
        var track = TestTrack;
        var entry = new PlaylistEntry(EntryId.New(), track.Id);
        var playlist = new Playlist(PlaylistId.New(), "Main", [entry]);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        bus.Submit(new ClientId("setup"), 2, new EnqueueEntry(entry.Id));
        bus.Submit(new ClientId("setup"), 3, new EnqueueEntry(entry.Id));
        bus.Submit(new ClientId("setup"), 4, new Play());
        using var vm = new QueueViewModel(bus);

        Assert.Single(vm.Items);
        Assert.True(vm.Items[0].IsCurrent);
    }

    [Fact]
    public void RemoveSelected_RemovesByIndex()
    {
        var bus = LoadedBus();
        bus.Submit(new ClientId("setup"), 2, new EnqueueTrack(TestTrack.Id));
        bus.Submit(new ClientId("setup"), 3, new EnqueueTrack(TestTrack.Id));
        using var vm = new QueueViewModel(bus);
        vm.Selected = vm.Items[0];

        vm.RemoveSelectedCommand.Execute(null);

        Assert.Single(vm.Items);
    }

    [Fact]
    public void RemoveItem_RemovesGivenRow()
    {
        var bus = LoadedBus();
        bus.Submit(new ClientId("setup"), 2, new EnqueueTrack(TestTrack.Id));
        bus.Submit(new ClientId("setup"), 3, new EnqueueTrack(TestTrack.Id));
        using var vm = new QueueViewModel(bus);

        vm.RemoveItem(vm.Items[0]);

        Assert.Single(vm.Items);
    }

    [Fact]
    public void FocusPlaying_SelectsCurrent()
    {
        var track = TestTrack;
        var entry = new PlaylistEntry(EntryId.New(), track.Id);
        var playlist = new Playlist(PlaylistId.New(), "Main", [entry]);
        var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [playlist], playlist.Id));
        bus.Submit(new ClientId("setup"), 2, new EnqueueEntry(entry.Id));
        bus.Submit(new ClientId("setup"), 3, new EnqueueEntry(entry.Id));
        bus.Submit(new ClientId("setup"), 4, new Play());
        using var vm = new QueueViewModel(bus);
        vm.Selected = null;

        vm.FocusPlaying();

        Assert.NotNull(vm.Selected);
        Assert.True(vm.Selected.IsCurrent);
    }
}
