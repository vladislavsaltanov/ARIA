namespace Aria.Remote.Monitor;

using System.Collections.Immutable;
using System.Text;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.State;

internal sealed class MonitorTimelineBuilder
{
    private const int SampleRate = 48000;
    private const long HeartbeatUs = 500_000;
    private const long EndedGraceUs = 1_000_000;
    private const long AllowedWindowUs = 30_000_000;

    private readonly Func<StreamHandle, DeckContent?> _bindings;
    private readonly IMonitorSources _sources;
    private readonly Func<TrackId, bool> _available;
    private readonly SortedDictionary<int, VoiceState> _voices = [];
    private readonly Dictionary<int, DeckContent> _bindingCache = [];
    private readonly Dictionary<TrackId, long> _seenTracks = [];
    private TransportState? _transport;
    private bool _testClick;
    private long _testOutputFrame;
    private int _testSinkGeneration;
    private long _seq;
    private string? _lastSignature;
    private long _lastSentUs = long.MinValue;
    private ImmutableHashSet<TrackId> _allowed = [];

    public MonitorTimelineBuilder(Func<StreamHandle, DeckContent?> bindings, IMonitorSources sources, Func<TrackId, bool> available)
    {
        _bindings = bindings;
        _sources = sources;
        _available = available;
    }

    public ImmutableHashSet<TrackId> AllowedTracks => Volatile.Read(ref _allowed);

    public void Apply(MixBlockReport report)
    {
        for (var index = 0; index < report.Count; index++)
        {
            var voice = report.Voices[index];
            if (_bindings(new StreamHandle(voice.Tag)) is { } deck)
            {
                _bindingCache[voice.Tag] = deck;
            }
            _voices[voice.Tag] = new VoiceState(voice, report.OutputFrame, report.SinkGeneration);
        }
        _testClick = report.TestClick;
        _testOutputFrame = report.OutputFrame;
        _testSinkGeneration = report.SinkGeneration;
    }

    public void SetTransport(TransportState transport) => _transport = transport;

    public TimelineMessage? Build(OutputClock clock, long nowUs, bool force)
    {
        if (!clock.IsSettled)
        {
            return null;
        }
        var rate = clock.RateHz;
        var voices = ImmutableArray.CreateBuilder<VoiceTimeline>();
        var gridCandidates = new List<(int Tag, BeatGrid Grid, long CueInFrames)>();
        var expired = new List<int>();
        foreach (var (tag, state) in _voices)
        {
            if (!_bindingCache.TryGetValue(tag, out var deck)
                || !clock.TryPresentAtUs(state.OutputFrame, state.SinkGeneration, out var anchorUs))
            {
                continue;
            }
            var report = state.Report;
            var cueInFrames = Frames(deck.CueIn);
            var anchorFrame = cueInFrames + report.SourceFrame;
            long? endFrame = report.Ended ? cueInFrames + report.EndSourceFrame : null;
            if (endFrame is { } end && nowUs > anchorUs + (long)((end - anchorFrame) * 1_000_000.0 / rate) + EndedGraceUs)
            {
                expired.Add(tag);
                continue;
            }
            var (track, kind, fallback, gainDb) = Resolve(deck.TrackId, deck.Backing, report.GainDb);
            voices.Add(new VoiceTimeline(
                tag,
                report.Epoch,
                track.Value,
                kind,
                fallback,
                gainDb,
                anchorUs,
                anchorFrame,
                report.Active,
                endFrame,
                FadeOf(report)));
            MarkSeen(track, nowUs);
            if (!report.StopWhenDone && !report.Ended && GridOf(deck) is { } grid)
            {
                gridCandidates.Add((tag, grid, cueInFrames));
            }
        }
        foreach (var tag in expired)
        {
            _voices.Remove(tag);
            _bindingCache.Remove(tag);
        }
        GridTimeline? gridTimeline = null;
        if (gridCandidates.Count > 0)
        {
            var newest = gridCandidates.MaxBy(candidate => candidate.Tag);
            gridTimeline = new GridTimeline(newest.Tag, newest.Grid.Bpm, newest.Grid.BeatsPerBar, Frames(newest.Grid.FirstBeat));
        }
        var deckTimeline = BuildDeck(nowUs);
        var test = BuildTest(clock, rate);
        PublishAllowed(nowUs);
        var built = voices.ToImmutable();
        var signature = Signature(built, gridTimeline, deckTimeline, test);
        if (!force && signature == _lastSignature && nowUs - _lastSentUs < HeartbeatUs)
        {
            return null;
        }
        _lastSignature = signature;
        _lastSentUs = nowUs;
        return new TimelineMessage("timeline", ++_seq, rate, built, gridTimeline, deckTimeline, test);
    }

    private (TrackId Track, string Kind, bool Fallback, double GainDb) Resolve(TrackId entryTrack, TrackId? backing, double entryGainDb)
    {
        if (backing is not { } backingId)
        {
            return (entryTrack, "entry", false, entryGainDb);
        }
        if (_available(backingId) && _sources.Find(backingId) is { } source)
        {
            return (backingId, "backing", false, source.GainDb);
        }
        return (entryTrack, "entry", true, entryGainDb);
    }

    private BeatGrid? GridOf(DeckContent deck) =>
        deck.Grid ?? (deck.Backing is { } backing ? _sources.Find(backing)?.Grid : null);

    private DeckTimeline BuildDeck(long nowUs)
    {
        var status = _transport?.Status.ToString() ?? nameof(TransportStatus.Stopped);
        NextTimeline? next = null;
        if (_transport?.Next is { } upcoming)
        {
            var (track, kind, _, _) = Resolve(upcoming.TrackId, upcoming.Backing, 0);
            next = new NextTimeline(upcoming.DisplayName, track.Value, kind, Frames(upcoming.CueIn));
            MarkSeen(track, nowUs);
        }
        return new DeckTimeline(status, _transport?.Current?.DisplayName, next);
    }

    private TestTimeline? BuildTest(OutputClock clock, double rate)
    {
        if (!_testClick)
        {
            return null;
        }
        var nextMultiple = (_testOutputFrame + SampleRate - 1) / SampleRate * SampleRate;
        return clock.TryPresentAtUs(nextMultiple, _testSinkGeneration, out var anchorUs)
            ? new TestTimeline(anchorUs, SampleRate / rate * 1_000_000.0)
            : null;
    }

    private void MarkSeen(TrackId track, long nowUs) => _seenTracks[track] = nowUs;

    private void PublishAllowed(long nowUs)
    {
        foreach (var track in _seenTracks.Where(pair => nowUs - pair.Value > AllowedWindowUs).Select(pair => pair.Key).ToList())
        {
            _seenTracks.Remove(track);
        }
        Volatile.Write(ref _allowed, [.. _seenTracks.Keys]);
    }

    private static FadeTimeline? FadeOf(VoiceReport report)
    {
        if (report.FadeProgress >= 1.0 && Math.Abs(report.FadeTo - 1.0) < 1e-9)
        {
            return null;
        }
        return new FadeTimeline(report.FadeFrom, report.FadeTo, report.FadeProgress, report.FadeFrames, report.FadeCurve.ToString(), report.StopWhenDone);
    }

    private static long Frames(TimeSpan time) => time.Ticks * SampleRate / TimeSpan.TicksPerSecond;

    private static string Signature(ImmutableArray<VoiceTimeline> voices, GridTimeline? grid, DeckTimeline deck, TestTimeline? test)
    {
        var builder = new StringBuilder();
        foreach (var voice in voices)
        {
            builder.Append(voice.Id).Append(':').Append(voice.Epoch).Append(':').Append(voice.Playing).Append(':').Append(voice.EndFrame)
                .Append(':').Append(voice.Track).Append(':').Append(voice.Kind).Append(':').Append(voice.Fallback);
            if (voice.Fade is { } fade)
            {
                builder.Append(':').Append(fade.To).Append(':').Append(fade.Frames).Append(':').Append(fade.Curve).Append(':').Append(fade.StopAtEnd);
            }
            builder.Append('|');
        }
        builder.Append(grid).Append('|').Append(deck).Append('|').Append(test is not null);
        return builder.ToString();
    }

    private sealed record VoiceState(VoiceReport Report, long OutputFrame, int SinkGeneration);
}
