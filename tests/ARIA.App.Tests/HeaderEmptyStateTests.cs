namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;

public sealed class HeaderEmptyStateTests
{
    private static readonly Track TestTrack = new(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());

    [Fact]
    public void NothingOnAir_ShowsHintWithNextTrack_PlayHidesIt()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var project = new Project(ProjectId.New(), "Main", [new ProjectEntry(EntryId.New(), TestTrack.Id)]);
        bus.Submit(new ClientId("setup"), 1, new LoadShow([TestTrack], [project], project.Id));
        using var vm = new TransportViewModel(bus);

        Assert.False(vm.HasCurrent);
        Assert.Equal("Нет трека в эфире · Далее: test", vm.IdleHint);

        vm.PlayCommand.Execute(null);

        Assert.True(vm.HasCurrent);
    }

    [Fact]
    public void EmptyShow_HintSaysWhatToDoNext()
    {
        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        using var vm = new TransportViewModel(bus);

        Assert.False(vm.HasCurrent);
        Assert.Equal("Нет трека в эфире · перетащите файлы в плейлист", vm.IdleHint);
    }
}
