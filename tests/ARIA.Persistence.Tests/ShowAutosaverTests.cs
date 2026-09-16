namespace Aria.Persistence.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class ShowAutosaverTests : IDisposable
{
    private static readonly ClientId Client = new("autosave-test");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aria-autosave-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var suffix in new[] { "", ".tmp" })
        {
            var file = _path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Autosaver_SavesAfterDebounce()
    {
        using var store = new JsonSnapshotStore(_path);
        using var bus = NewBus();
        using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromMilliseconds(150));

        bus.Submit(Client, 1, new CreateProject("Main"));

        var document = WaitForDocument(store, TimeSpan.FromSeconds(5));
        Assert.NotNull(document);
        Assert.Single(document.Projects);
        Assert.Equal("Main", document.Projects[0].Name);
    }

    [Fact]
    public void Autosaver_Debounces_Burst()
    {
        using var store = new JsonSnapshotStore(_path);
        using var bus = NewBus();
        using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromMilliseconds(150));

        for (var i = 1; i <= 5; i++)
        {
            bus.Submit(Client, i, new CreateProject($"p{i}"));
        }

        ShowDocument? document = null;
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            document = store.LoadLatest();
            if (document is { } found && found.Projects.Length == 5)
            {
                break;
            }
            Thread.Sleep(20);
        }

        Assert.NotNull(document);
        Assert.Equal(5, document.Projects.Length);
    }

    [Fact]
    public void Autosaver_IgnoresTransportOnly()
    {
        using var store = new JsonSnapshotStore(_path);
        using var bus = NewBus();
        using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromMilliseconds(150));

        bus.Submit(Client, 1, new Pause());

        Thread.Sleep(600);
        Assert.Null(store.LoadLatest());
    }

    [Fact]
    public void FlushNow_SavesImmediately()
    {
        using var store = new JsonSnapshotStore(_path);
        using var bus = NewBus();
        using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromMilliseconds(150));

        bus.Submit(Client, 1, new CreateProject("Main"));
        autosaver.FlushNow();

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && store.LoadLatest() is null)
        {
            Thread.Sleep(20);
        }

        Assert.NotNull(store.LoadLatest());
    }

    [Fact]
    public void TransportDeltas_DoNotTriggerAutosave()
    {
        using var store = new JsonSnapshotStore(_path);
        using var bus = NewBus();
        using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromMilliseconds(150));

        bus.Submit(Client, 1, new Pause());

        Thread.Sleep(500);
        Assert.Null(store.LoadLatest());
    }

    [Fact]
    public void BackgroundFlush_MissingDirectory_DoesNotCrash_AndRecovers()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aria-autosave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            using var store = new JsonSnapshotStore(Path.Combine(dir, "show.json"));
            using var bus = NewBus();
            using var autosaver = new ShowAutosaver(bus, store, TimeSpan.FromMilliseconds(50));

            bus.Submit(Client, 1, new CreateProject("Main"));
            Directory.Delete(dir, recursive: true);
            Thread.Sleep(400);
            Directory.CreateDirectory(dir);
            bus.Submit(Client, 2, new CreateProject("Spare"));

            var document = WaitForDocument(store, TimeSpan.FromSeconds(5));
            Assert.NotNull(document);
            Assert.Equal(2, document.Projects.Length);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static CommandBus NewBus() => new(new ShowController(new StubEngine()));

    private static ShowDocument? WaitForDocument(JsonSnapshotStore store, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (store.LoadLatest() is { } document)
            {
                return document;
            }
            Thread.Sleep(20);
        }
        return store.LoadLatest();
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
