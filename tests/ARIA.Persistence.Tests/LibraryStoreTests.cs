namespace Aria.Persistence.Tests;

using System.Collections.Immutable;
using Aria.Core.Model;

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aria-lib-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = _path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public void RoundTrip_PreservesTracksPlaylistsAndOverrides()
    {
        var marker = new Marker("storm-end", TimeSpan.FromSeconds(90), MarkerAction.Stop);
        var t1 = new Track(
            TrackId.New(), "/audio/one.flac", "one", TimeSpan.FromMinutes(3),
            new TrackDefaults(GainDb: -2, EndAction: EndAction.Pause,
                In: new Fade(TimeSpan.FromMilliseconds(300), FadeCurve.Linear),
                Out: new Fade(TimeSpan.FromSeconds(2), FadeCurve.SCurve),
                Markers: [marker]));
        var t2 = new Track(TrackId.New(), "/audio/two.wav", "two", TimeSpan.FromSeconds(45), new TrackDefaults());
        var overrides = new PlaylistOverrides(
            Name: "Буря, акт 2", Color: "amber", Note: "примечание",
            GainDb: -6, EndAction: EndAction.Replay,
            In: new Fade(TimeSpan.FromSeconds(1), FadeCurve.Logarithmic),
            CueIn: TimeSpan.FromSeconds(10), CueOut: TimeSpan.FromMinutes(1));
        var e1 = new PlaylistEntry(EntryId.New(), t1.Id, overrides);
        var e2 = new PlaylistEntry(EntryId.New(), t2.Id);
        var p1 = new Playlist(PlaylistId.New(), "Спектакль", [e1, e2]);
        var p2 = new Playlist(PlaylistId.New(), "Антракт", []);

        using (var store = new SqliteLibraryStore(_path))
        {
            store.Upsert([t1, t2], [p1, p2]);
        }

        using (var store = new SqliteLibraryStore(_path))
        {
            var (tracks, playlists) = store.Load();

            Assert.Equal(2, tracks.Length);
            var loaded1 = tracks.Single(t => t.Id == t1.Id);
            Assert.Equal(t1.FilePath, loaded1.FilePath);
            Assert.Equal(t1.DefaultName, loaded1.DefaultName);
            Assert.Equal(t1.Duration, loaded1.Duration);
            Assert.Equal(-2, loaded1.Defaults.GainDb);
            Assert.Equal(EndAction.Pause, loaded1.Defaults.EndAction);
            Assert.Equal(TimeSpan.FromMilliseconds(300), loaded1.Defaults.In!.Duration);
            Assert.Equal(FadeCurve.Linear, loaded1.Defaults.In.Curve);
            Assert.Equal(TimeSpan.FromSeconds(2), loaded1.Defaults.Out!.Duration);
            Assert.Equal(FadeCurve.SCurve, loaded1.Defaults.Out.Curve);
            var loadedMarker = loaded1.Defaults.Markers!.Value.Single();
            Assert.Equal("storm-end", loadedMarker.Name);
            Assert.Equal(TimeSpan.FromSeconds(90), loadedMarker.Position);
            Assert.Equal(MarkerAction.Stop, loadedMarker.Action);

            var t2defaults = tracks.Single(t => t.Id == t2.Id).Defaults;
            Assert.Equal(0, t2defaults.GainDb);
            Assert.Null(t2defaults.Markers);

            Assert.Equal(2, playlists.Length);
            Assert.Equal("Спектакль", playlists[0].Name);
            Assert.Equal(2, playlists[0].Entries.Length);

            var loadedE1 = playlists[0].Entries[0];
            Assert.Equal(e1.Id, loadedE1.Id);
            Assert.Equal(t1.Id, loadedE1.TrackId);
            Assert.NotNull(loadedE1.Overrides);
            Assert.Equal("Буря, акт 2", loadedE1.Overrides!.Name);
            Assert.Equal("amber", loadedE1.Overrides.Color);
            Assert.Equal("примечание", loadedE1.Overrides.Note);
            Assert.Equal(-6, loadedE1.Overrides.GainDb);
            Assert.Equal(EndAction.Replay, loadedE1.Overrides.EndAction);
            Assert.Equal(TimeSpan.FromSeconds(1), loadedE1.Overrides.In!.Duration);
            Assert.Equal(TimeSpan.FromSeconds(10), loadedE1.Overrides.CueIn);
            Assert.Equal(TimeSpan.FromMinutes(1), loadedE1.Overrides.CueOut);

            var loadedE2 = playlists[0].Entries[1];
            Assert.Equal(e2.Id, loadedE2.Id);
            Assert.Null(loadedE2.Overrides);

            Assert.Equal("Антракт", playlists[1].Name);
            Assert.Empty(playlists[1].Entries);
        }
    }

    [Fact]
    public void Upsert_RemovesMissingPlaylistsAndOrphanEntries()
    {
        var t1 = TestFactory.Track("one");
        var p1 = TestFactory.Playlist("First", TestFactory.Entry(t1));
        var p2 = TestFactory.Playlist("Second", TestFactory.Entry(t1));

        using (var store = new SqliteLibraryStore(_path))
        {
            store.Upsert([t1], [p1, p2]);
        }
        using (var store = new SqliteLibraryStore(_path))
        {
            store.Upsert([t1], [p1]);
        }
        using (var store = new SqliteLibraryStore(_path))
        {
            var (_, playlists) = store.Load();

            var playlist = Assert.Single(playlists);
            Assert.Equal(p1.Id, playlist.Id);
            var entry = Assert.Single(playlist.Entries);
            Assert.Equal(p1.Entries[0].Id, entry.Id);
        }
    }

    [Fact]
    public void Upsert_RemovesMissingTracks()
    {
        var t1 = TestFactory.Track("one");
        var t2 = TestFactory.Track("two");
        var p1 = TestFactory.Playlist("Main", TestFactory.Entry(t1));

        using (var store = new SqliteLibraryStore(_path))
        {
            store.Upsert([t1, t2], [p1]);
        }
        using (var store = new SqliteLibraryStore(_path))
        {
            store.Upsert([t1], [p1]);
        }
        using (var store = new SqliteLibraryStore(_path))
        {
            var (tracks, _) = store.Load();

            var track = Assert.Single(tracks);
            Assert.Equal(t1.Id, track.Id);
        }
    }

    [Fact]
    public void EmptyLibrary_RoundTrips()
    {
        using var store = new SqliteLibraryStore(_path);
        store.Upsert([], []);

        var (tracks, playlists) = store.Load();

        Assert.Empty(tracks);
        Assert.Empty(playlists);
    }
}
