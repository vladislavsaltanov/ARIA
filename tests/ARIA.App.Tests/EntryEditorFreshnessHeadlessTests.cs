namespace Aria.App.Tests;

using Aria.App.ViewModels;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using Avalonia.Headless;
using Avalonia.Threading;

[Collection("headless")]
public sealed class EntryEditorFreshnessHeadlessTests
{
    private readonly HeadlessUnitTestSession _session;

    public EntryEditorFreshnessHeadlessTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
    }

    [Fact]
    public async Task ImmediateReopen_AfterEdit_ShowsFreshValue()
    {
        await _session.Dispatch(() =>
        {
            var track = new Track(TrackId.New(), "/audio/test.flac", "test", TimeSpan.FromMinutes(3), new TrackDefaults());
            var entry = new ProjectEntry(EntryId.New(), new TrackContent(track.Id));
            var project = new Project(ProjectId.New(), "Main", [entry]);
            using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Pumped);
            bus.Submit(new ClientId("setup"), 1, new LoadShow([track], [project], project.Id));
            using var vm = new ProjectsViewModel(bus, () => [track], sync: SynchronizationContext.Current);

            WaitForController(() => bus.Snapshot().Show.Projects.Length > 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(vm.Projects);

            var editor1 = vm.CreateEntryAudioEditor(vm.Projects[0].Entries[0]);
            editor1.EqBands[3].GainDb = -5;
            WaitForController(() => ((TrackContent)bus.Snapshot().Show.Projects[0].Entries[0].Content).Overrides?.Audio is { } a && a.Eq.Bands[3].GainDb == -5);

            var editor2 = vm.CreateEntryAudioEditor(vm.Projects[0].Entries[0]);

            Assert.False(editor2.InheritTrackSettings);
            Assert.Equal(-5, editor2.EqBands[3].GainDb);
            return 0;
        }, CancellationToken.None);
    }

    private static void WaitForController(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            Thread.Sleep(10);
        }
        throw new TimeoutException("controller condition not met");
    }
}
