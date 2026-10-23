namespace Aria.Core.Tests;

using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;

public sealed class BackingOverrideTests : IDisposable
{
    private static readonly ClientId TestClient = new("backing");

    private readonly FakeEngine _engine = new();
    private readonly CommandBus _bus;
    private readonly Track _song;
    private readonly Track _backing;
    private readonly Track _other;
    private readonly ProjectEntry _songEntry;
    private readonly System.Collections.Concurrent.ConcurrentQueue<StateEvent> _events = new();
    private readonly IDisposable _subscription;
    private long _seq;

    public BackingOverrideTests()
    {
        _bus = new CommandBus(new ShowController(_engine), BusMode.Pumped);
        _subscription = _bus.Subscribe(e => _events.Enqueue(e));
        _song = TestShow.Track("song");
        _backing = TestShow.Track("song-guide");
        _other = TestShow.Track("other");
        _songEntry = TestShow.Entry(_song);
        var project = TestShow.Project("Main", _songEntry, TestShow.Entry(_other));
        Submit(new LoadShow([_song, _backing, _other], [project], project.Id));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _bus.Dispose();
    }

    [Fact]
    public async Task SetEntryOverrides_WithBacking_ResolvesIntoCurrentAndNext()
    {
        Submit(new SetEntryOverrides(_songEntry.Id, new ProjectOverrides(Backing: _backing.Id)));
        await Poll(() => _bus.Snapshot().Transport.Next?.Backing == _backing.Id, "next deck has no backing");

        Submit(new Play());
        await Poll(() => _bus.Snapshot().Transport.Current?.Backing == _backing.Id, "current deck has no backing");
        Assert.Equal(_song.Id, _bus.Snapshot().Transport.Current!.TrackId);
    }

    [Fact]
    public async Task SetEntryOverrides_WhilePlaying_UpdatesCurrentBacking()
    {
        Submit(new Play());
        await Poll(() => _bus.Snapshot().Transport.Status == TransportStatus.Playing, "not playing");

        Submit(new SetEntryOverrides(_songEntry.Id, new ProjectOverrides(Backing: _backing.Id)));

        await Poll(() => _bus.Snapshot().Transport.Current?.Backing == _backing.Id, "current deck kept old backing");
    }

    [Fact]
    public async Task SetEntryOverrides_UnknownBacking_Rejected()
    {
        var seq = Interlocked.Increment(ref _seq);
        _bus.Submit(TestClient, seq, new SetEntryOverrides(_songEntry.Id, new ProjectOverrides(Name: "x", Backing: TrackId.New())));

        await Poll(() => _events.Any(e => e is Rejected r && r.Seq == seq && r.Reason == "unknown-backing"), "unknown backing accepted");
        Assert.Null(_bus.Snapshot().Show.Projects[0].Entries[0].Overrides);
    }

    [Fact]
    public async Task PlayTrack_WithoutEntry_HasNoBacking()
    {
        Submit(new SetEntryOverrides(_songEntry.Id, new ProjectOverrides(Backing: _backing.Id)));
        Submit(new PlayTrack(_song.Id));

        await Poll(() => _bus.Snapshot().Transport.Status == TransportStatus.Playing, "not playing");
        Assert.Null(_bus.Snapshot().Transport.Current!.Backing);
    }

    [Fact]
    public async Task Digest_IncludesBackingTrack()
    {
        Assert.DoesNotContain(_bus.Snapshot().Show.TrackDigest.Entries, e => e.Track == _backing.Id);

        Submit(new SetEntryOverrides(_songEntry.Id, new ProjectOverrides(Backing: _backing.Id)));

        await Poll(() => _bus.Snapshot().Show.TrackDigest.Entries.Any(e => e.Track == _backing.Id), "digest missing backing track");
    }

    private void Submit(Command command) => _bus.Submit(TestClient, Interlocked.Increment(ref _seq), command);

    private static async Task Poll(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail(message);
            }
            await Task.Delay(25);
        }
    }
}
