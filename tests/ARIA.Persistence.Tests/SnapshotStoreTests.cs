namespace Aria.Persistence.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.State;

public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aria-snap-{Guid.NewGuid():N}.json");

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
    public void LoadLatest_OnEmptyStore_ReturnsNull()
    {
        using var store = new JsonSnapshotStore(_path);

        Assert.Null(store.LoadLatest());
    }

    [Fact]
    public void RoundTrip_PreservesDocument()
    {
        var t1 = TestFactory.Track("one", EndAction.Pause);
        var e1 = TestFactory.Entry(t1);
        var p1 = TestFactory.Playlist("Main", e1);
        var queue = ImmutableArray.Create(
            new QueueItem(e1.Id, t1.Id, "из очереди", "amber"),
            new QueueItem(null, t1.Id, "без вхождения", null));
        var document = new ShowDocument(
            [t1], [p1], p1.Id, queue, -3.5, TimeSpan.FromMilliseconds(80), TimeSpan.FromMinutes(3), true, DateTimeOffset.UtcNow);

        using (var store = new JsonSnapshotStore(_path))
        {
            store.Save(document);
        }

        using (var store = new JsonSnapshotStore(_path))
        {
            var loaded = store.LoadLatest();

            Assert.NotNull(loaded);
            var track = Assert.Single(loaded.Tracks);
            Assert.Equal(t1.Id, track.Id);
            Assert.Equal(EndAction.Pause, track.Defaults.EndAction);
            var playlist = Assert.Single(loaded.Playlists);
            Assert.Equal(p1.Id, playlist.Id);
            Assert.Equal(p1.Entries[0].Id, playlist.Entries[0].Id);
            Assert.Equal(p1.Id, loaded.ActiveId);
            Assert.Equal(2, loaded.Queue.Length);
            Assert.Equal(e1.Id, loaded.Queue[0].EntryId);
            Assert.Equal("из очереди", loaded.Queue[0].DisplayName);
            Assert.Equal("amber", loaded.Queue[0].Color);
            Assert.Null(loaded.Queue[1].EntryId);
            Assert.Null(loaded.Queue[1].Color);
            Assert.Equal(-3.5, loaded.MasterGainDb);
            Assert.Equal(TimeSpan.FromMilliseconds(80), loaded.PanicFade);
            Assert.Equal(TimeSpan.FromMinutes(3), loaded.ClockElapsed);
            Assert.True(loaded.ClockRunning);
            Assert.Equal(document.SavedAt, loaded.SavedAt);
        }
    }

    [Fact]
    public void Save_Twice_LoadLatestReturnsSecondDocument()
    {
        var t1 = TestFactory.Track("one");
        var first = new ShowDocument([t1], [], null, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, DateTimeOffset.UtcNow);
        var second = new ShowDocument([t1], [], null, [], -7, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, DateTimeOffset.UtcNow);

        using (var store = new JsonSnapshotStore(_path))
        {
            store.Save(first);
            store.Save(second);
        }

        using (var store = new JsonSnapshotStore(_path))
        {
            var loaded = store.LoadLatest();

            Assert.NotNull(loaded);
            Assert.Equal(-7, loaded.MasterGainDb);
        }
    }

    [Fact]
    public void Save_DoesNotLeaveTempFile()
    {
        using (var store = new JsonSnapshotStore(_path))
        {
            var t1 = TestFactory.Track("one");
            store.Save(new ShowDocument([t1], [], null, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, DateTimeOffset.UtcNow));
        }

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void CorruptedFile_ReturnsNull_WithoutThrowing()
    {
        File.WriteAllText(_path, "{ this is not json");
        using var store = new JsonSnapshotStore(_path);

        Assert.Null(store.LoadLatest());
    }
}
