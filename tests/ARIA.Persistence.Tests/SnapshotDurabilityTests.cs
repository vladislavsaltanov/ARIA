namespace Aria.Persistence.Tests;

using System.Collections.Immutable;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class SnapshotDurabilityTests : IDisposable
{
    private static readonly ClientId Client = new("durability");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-durable-{Guid.NewGuid():N}");

    public SnapshotDurabilityTests() => Directory.CreateDirectory(_directory);

    private string ShowPath => Path.Combine(_directory, "show.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Save_KeepsPreviousGenerationAsBackup()
    {
        using var store = new JsonSnapshotStore(ShowPath);
        store.Save(Document("first"));
        store.Save(Document("second"));

        Assert.True(File.Exists(ShowPath + ".bak"));
        Assert.Equal("second", store.LoadLatest()!.Projects[0].Name);
    }

    [Fact]
    public void CorruptMainFile_FallsBackToBackup_AndQuarantinesCorruptCopy()
    {
        using var store = new JsonSnapshotStore(ShowPath);
        store.Save(Document("first"));
        store.Save(Document("second"));
        File.WriteAllText(ShowPath, "{ \"Tracks\": [ {\"Id\": ");

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Equal("first", loaded.Projects[0].Name);
        var quarantined = Directory.GetFiles(_directory, "show.json.corrupt-*");
        Assert.Single(quarantined);
        Assert.StartsWith("{ \"Tracks\"", File.ReadAllText(quarantined[0]));
    }

    [Fact]
    public void NullCollectionsInJson_DoNotThrow()
    {
        File.WriteAllText(ShowPath, "{\"Tracks\":null,\"Projects\":null,\"Queue\":null}");
        using var store = new JsonSnapshotStore(ShowPath);

        var loaded = store.LoadLatest();

        Assert.NotNull(loaded);
        Assert.Empty(loaded.Tracks);
        Assert.Empty(loaded.Projects);
        Assert.Empty(loaded.Queue);
    }

    [Fact]
    public void MissingMainFile_WithBackup_RestoresBackup()
    {
        using var store = new JsonSnapshotStore(ShowPath);
        store.Save(Document("first"));
        store.Save(Document("second"));
        File.Delete(ShowPath);

        Assert.Equal("first", store.LoadLatest()!.Projects[0].Name);
    }

    [Fact]
    public void Repair_InconsistentDocument_IsAcceptedByRestore()
    {
        var kept = TestFactory.Track("kept");
        var duplicate = kept with { DefaultName = "duplicate" };
        var keptEntry = TestFactory.Entry(kept);
        var dangling = new ProjectEntry(EntryId.New(), TrackId.New());
        var project = new Project(ProjectId.New(), "Main", [keptEntry, dangling, keptEntry]);
        var document = new ShowDocument(
            [kept, duplicate],
            [project],
            ProjectId.New(),
            [new QueueItem(dangling.Id, dangling.TrackId, "ghost", null), new QueueItem(keptEntry.Id, kept.Id, "ok", null)],
            99,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(-5),
            false,
            [],
            DateTimeOffset.UtcNow);

        var repaired = ShowDocumentRepair.Repair(document);

        using var bus = new CommandBus(new ShowController(new StubEngine()), BusMode.Inline);
        var rejections = new List<Rejected>();
        bus.Subscribe(e =>
        {
            if (e is Rejected r)
            {
                rejections.Add(r);
            }
        });
        bus.Submit(Client, 1, new RestoreShow(repaired.Tracks, repaired.Projects, repaired.ActiveId, repaired.Queue, repaired.MasterGainDb, repaired.PanicFade, repaired.ClockElapsed, repaired.ClockRunning, repaired.Scripts));

        Assert.Empty(rejections);
        var restored = Assert.Single(bus.Snapshot().Show.Projects);
        Assert.Equal([keptEntry.Id], restored.Entries.Select(e => e.Id));
        Assert.Single(bus.Snapshot().Queue.Items);
    }

    [Fact]
    public void Repair_ConsistentDocument_IsUnchanged()
    {
        var document = Document("same");

        Assert.Same(document, ShowDocumentRepair.Repair(document));
    }

    [Fact]
    public void Autosaver_UnchangedLibrary_IsNotRewrittenOnEveryClockTick()
    {
        using var store = new JsonSnapshotStore(ShowPath);
        var controller = new ShowController(new StubEngine());
        using var bus = new CommandBus(controller, BusMode.Inline);
        var library = new CountingLibraryStore();
        using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromHours(1), () => controller.Tracks, library);
        var track = TestFactory.Track("tick");
        var project = TestFactory.Project("Main", TestFactory.Entry(track));
        bus.Submit(Client, 1, new LoadShow([track], [project], project.Id));
        bus.Submit(Client, 2, new StartShowClock());

        autosaver.FlushNow();
        for (var seq = 3; seq < 10; seq++)
        {
            bus.Submit(Client, seq, new TickShowClock());
            autosaver.FlushNow();
        }
        Assert.Equal(1, library.Upserts);

        bus.Submit(Client, 20, new RenameProject(project.Id, "Renamed"));
        autosaver.FlushNow();
        Assert.Equal(2, library.Upserts);
    }

    private static ShowDocument Document(string projectName)
    {
        var track = TestFactory.Track(projectName);
        var project = TestFactory.Project(projectName, TestFactory.Entry(track));
        return new ShowDocument([track], [project], project.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow);
    }

    private sealed class CountingLibraryStore : ILibraryStore
    {
        public int Upserts { get; private set; }

        public void Upsert(ImmutableArray<Track> tracks, ImmutableArray<Project> projects) => Upserts++;

        public (ImmutableArray<Track> Tracks, ImmutableArray<Project> Projects) Load() => ([], []);

        public void Dispose()
        {
        }
    }

    private sealed class StubEngine : IAudioEngine
    {
        public event Action<StreamEvent>? Events
        {
            add { }
            remove { }
        }

        public StreamHandle StartStream(TrackSource source, StreamOptions options) => new(0);

        public void Transport(StreamHandle handle, TransportCommand command)
        {
        }

        public void SetMix(StreamHandle handle, MixParameters mix)
        {
        }

        public void Seek(StreamHandle handle, TimeSpan position)
        {
        }

        public void SetMasterGain(double gainDb)
        {
        }

        public void SetSmoothing(Smoothing smoothing)
        {
        }

        public void Panic(PanicSpec spec)
        {
        }

        public void DisposeStream(StreamHandle handle)
        {
        }
    }
}
