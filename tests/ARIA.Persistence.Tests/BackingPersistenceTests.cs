namespace Aria.Persistence.Tests;

using Aria.Core.Model;

public sealed class BackingPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aria-lib-backing-{Guid.NewGuid():N}.db");
    private readonly string _snapPath = Path.Combine(Path.GetTempPath(), $"aria-snap-backing-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _snapPath, _snapPath + ".tmp" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void Library_Backing_RoundTrips()
    {
        var song = TestFactory.Track("song");
        var guide = TestFactory.Track("guide");
        var project = TestFactory.Project("Main", TestFactory.Entry(song, new ProjectOverrides(Name: "Песня", Backing: guide.Id)));
        using (var store = new SqliteLibraryStore(_dbPath))
        {
            store.Upsert([song, guide], [project]);
        }

        using (var store = new SqliteLibraryStore(_dbPath))
        {
            var overrides = Assert.Single(Assert.Single(store.Load().Projects).Entries).Overrides;
            Assert.Equal(guide.Id, overrides!.Backing);
            Assert.Equal("Песня", overrides.Name);
        }
    }

    [Fact]
    public void Snapshot_Backing_RoundTrips()
    {
        var song = TestFactory.Track("song");
        var guide = TestFactory.Track("guide");
        var project = TestFactory.Project("Main", TestFactory.Entry(song, new ProjectOverrides(Backing: guide.Id)));
        var document = new ShowDocument([song, guide], [project], project.Id, [], 0, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false, [], DateTimeOffset.UtcNow);
        using (var store = new JsonSnapshotStore(_snapPath))
        {
            store.Save(document);
        }

        using (var store = new JsonSnapshotStore(_snapPath))
        {
            var loaded = store.LoadLatest();
            Assert.NotNull(loaded);
            Assert.Equal(guide.Id, Assert.Single(Assert.Single(loaded.Projects).Entries).Overrides!.Backing);
        }
    }
}
