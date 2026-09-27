namespace Aria.Audio;

using System.Collections.Concurrent;
using Aria.Core.Playback;

public readonly record struct SessionPumpContext(
    long MainTapHead,
    bool MainPlaying,
    bool SessionFollow,
    long PreviewGainBits,
    int PreviewMuted);

public sealed class SessionMixer : IDisposable
{
    private static readonly ClickSettings DefaultClick = new(120, 4, 0, 0);

    private readonly ISourceFactory _sourceFactory;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float[] _sessionScratch;
    private readonly float[] _clickScratch;
    private readonly object _sessionLock = new();
    private readonly ConcurrentQueue<ISampleSource> _retiredVoices = [];
    private PreviewSession[] _sessions = [];
    private int _handleCounter;

    public SessionMixer(ISourceFactory sourceFactory, int sampleRate, int channels, int blockSizeFrames)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSizeFrames);
        _sourceFactory = sourceFactory;
        _sampleRate = sampleRate;
        _channels = channels;
        _sessionScratch = new float[blockSizeFrames * channels];
        _clickScratch = new float[blockSizeFrames * channels];
    }

    public void Dispose()
    {
        while (_retiredVoices.TryDequeue(out var retired))
        {
            (retired as IDisposable)?.Dispose();
        }
    }

    internal static void RetireDisposable(object? disposable)
    {
        if (disposable is IDisposable target)
        {
            ThreadPool.QueueUserWorkItem(static state => ((IDisposable)state!).Dispose(), target);
        }
    }

    public PreviewSessionHandle Open(string? name, PreviewReader? reader, long seekFrames)
    {
        var handle = new PreviewSessionHandle(Interlocked.Increment(ref _handleCounter));
        var tap = new PreviewTap(Math.Max(256, _sampleRate * 2), _channels);
        var voice = new ClickVoice(_channels, _sampleRate, DefaultClick);
        var sessionName = string.IsNullOrWhiteSpace(name) ? $"Session {handle.Value}" : name.Trim();
        var session = new PreviewSession(handle, reader, voice, tap, sessionName);
        if (seekFrames >= 0)
        {
            voice.Seek(seekFrames);
        }
        lock (_sessionLock)
        {
            var grown = new PreviewSession[_sessions.Length + 1];
            Array.Copy(_sessions, grown, _sessions.Length);
            grown[_sessions.Length] = session;
            _sessions = grown;
        }
        return handle;
    }

    public void Close(PreviewSessionHandle session)
    {
        var target = FindSession(session);
        if (target?.TrackVoice is { } voice)
        {
            _retiredVoices.Enqueue(voice);
            Volatile.Write(ref target.TrackVoice, null);
        }
        lock (_sessionLock)
        {
            var kept = new PreviewSession[_sessions.Length];
            var count = 0;
            foreach (var candidate in _sessions)
            {
                if (candidate.Handle != session)
                {
                    kept[count++] = candidate;
                }
            }
            var shrunk = new PreviewSession[count];
            Array.Copy(kept, shrunk, count);
            _sessions = shrunk;
        }
    }

    public SourceOpenFault? StartTrack(PreviewSessionHandle session, TrackSource source)
    {
        if (FindSession(session) is not { } target)
        {
            return null;
        }
        if (!_sourceFactory.TryOpen(source.FilePath, source.CueIn, source.CueOut, out var sample, out var fault) || sample is null)
        {
            return fault;
        }
        var old = Volatile.Read(ref target.TrackVoice);
        Volatile.Write(ref target.TrackVoice, sample);
        if (old is not null)
        {
            _retiredVoices.Enqueue(old);
        }
        target.ResetVoice = true;
        return null;
    }

    public void SetClick(PreviewSessionHandle session, ClickSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (FindSession(session) is { } target)
        {
            target.Click = settings;
            target.PendingSettings = settings;
        }
    }

    public void SetClickMuted(PreviewSessionHandle session, bool muted)
    {
        if (FindSession(session) is { } target)
        {
            target.ClickMuted = muted;
        }
    }

    public void Rename(PreviewSessionHandle session, string name)
    {
        if (FindSession(session) is { } renamed)
        {
            renamed.Name = name;
        }
    }

    public void SetBackingGain(PreviewSessionHandle session, double gainDb)
    {
        if (FindSession(session) is { } target)
        {
            Volatile.Write(ref target.BackingGainBits, BitConverter.DoubleToInt64Bits(Math.Pow(10.0, gainDb / 20.0)));
        }
    }

    public void SetClickGain(PreviewSessionHandle session, double gainDb)
    {
        if (FindSession(session) is { } target)
        {
            var settings = target.Click ?? DefaultClick;
            target.Click = settings with { GainDb = gainDb };
            target.PendingSettings = target.Click;
        }
    }

    public PreviewTap? Tap(PreviewSessionHandle session) => FindSession(session)?.Tap;

    public IReadOnlyList<SessionProfile> List()
    {
        var sessions = Volatile.Read(ref _sessions);
        var profiles = new SessionProfile[sessions.Length];
        for (var i = 0; i < sessions.Length; i++)
        {
            profiles[i] = new SessionProfile(
                sessions[i].Handle,
                sessions[i].Name,
                20.0 * Math.Log10(Math.Max(double.Epsilon, BitConverter.Int64BitsToDouble(Volatile.Read(ref sessions[i].BackingGainBits)))),
                sessions[i].ClickMuted,
                sessions[i].Click?.GainDb ?? DefaultClick.GainDb);
        }
        return profiles;
    }

    public void ResetVoices()
    {
        foreach (var session in Volatile.Read(ref _sessions))
        {
            session.ResetVoice = true;
            Interlocked.Exchange(ref session.PendingSyncFrames, 0);
        }
    }

    public void SyncVoices(long frames)
    {
        foreach (var session in Volatile.Read(ref _sessions))
        {
            Interlocked.Exchange(ref session.PendingSyncFrames, frames);
        }
    }

    public void Pump(SessionPumpContext context)
    {
        while (_retiredVoices.TryDequeue(out var retired))
        {
            if (retired is IDisposable disposable)
            {
                RetireDisposable(disposable);
            }
        }
        var sessions = Volatile.Read(ref _sessions);
        if (sessions.Length == 0)
        {
            return;
        }
        foreach (var session in sessions)
        {
            PumpSession(session, context);
        }
    }

    private void PumpSession(PreviewSession session, SessionPumpContext context)
    {
        var voice = Volatile.Read(ref session.TrackVoice);
        if (voice is not null)
        {
            var voiceRead = voice.ReadFrames(_sessionScratch.AsSpan(0, _sessionScratch.Length)) * _channels;
            if (voiceRead > 0)
            {
                PublishSession(session, voiceRead, context);
                return;
            }
            _retiredVoices.Enqueue(voice);
            Volatile.Write(ref session.TrackVoice, null);
            var anchor = Math.Max(0, context.MainTapHead - (_sessionScratch.Length / _channels));
            session.Voice.Seek(anchor);
            session.Reader?.Seek(anchor);
        }
        if (!context.MainPlaying && !context.SessionFollow)
        {
            session.Reader?.ResetToHead();
            return;
        }
        if (session.Reader is null)
        {
            return;
        }
        var syncFrames = Interlocked.Exchange(ref session.PendingSyncFrames, -1);
        if (syncFrames >= 0)
        {
            session.Voice.Seek(syncFrames);
            session.Reader.Seek(Math.Max(0, context.MainTapHead - (_sessionScratch.Length / _channels)));
            session.ResetVoice = false;
        }
        else if (session.ResetVoice)
        {
            session.Voice.Seek(0);
            session.Reader.Seek(Math.Max(0, context.MainTapHead - (_sessionScratch.Length / _channels)));
            session.ResetVoice = false;
        }
        if (session.PendingSettings is { } settings)
        {
            session.Voice.UpdateSettings(settings);
            session.PendingSettings = null;
        }
        var read = session.Reader.Read(_sessionScratch.AsSpan(0, _sessionScratch.Length));
        if (read <= 0)
        {
            return;
        }
        PublishSession(session, read, context);
    }

    private void PublishSession(PreviewSession session, int read, SessionPumpContext context)
    {
        if (context.PreviewMuted == 1)
        {
            _sessionScratch.AsSpan(0, read).Clear();
        }
        else
        {
            var gain = (float)(BitConverter.Int64BitsToDouble(context.PreviewGainBits) * BitConverter.Int64BitsToDouble(Volatile.Read(ref session.BackingGainBits)));
            if (gain != 1f)
            {
                for (var i = 0; i < read; i++)
                {
                    _sessionScratch[i] *= gain;
                }
            }
        }
        if (session.ClickMuted)
        {
            session.Tap.Publish(_sessionScratch.AsSpan(0, read));
            return;
        }
        session.Voice.ReadFrames(_clickScratch.AsSpan(0, read));
        for (var i = 0; i < read; i++)
        {
            _sessionScratch[i] += _clickScratch[i];
        }
        session.Tap.Publish(_sessionScratch.AsSpan(0, read));
    }

    private PreviewSession? FindSession(PreviewSessionHandle session)
    {
        foreach (var candidate in Volatile.Read(ref _sessions))
        {
            if (candidate.Handle == session)
            {
                return candidate;
            }
        }
        return null;
    }

    private sealed class PreviewSession
    {
        public PreviewSession(PreviewSessionHandle handle, PreviewReader? reader, ClickVoice voice, PreviewTap tap, string name)
        {
            Handle = handle;
            Reader = reader;
            Voice = voice;
            Tap = tap;
            Name = name;
            BackingGainBits = BitConverter.DoubleToInt64Bits(1.0);
        }

        public PreviewSessionHandle Handle { get; }

        public PreviewReader? Reader { get; }

        public ClickVoice Voice { get; }

        public PreviewTap Tap { get; }

        public string Name;

        public long BackingGainBits;

        public volatile bool ClickMuted = true;

        public volatile ClickSettings? PendingSettings;

        public ClickSettings? Click;

        public volatile bool ResetVoice;

        public long PendingSyncFrames = -1;

        public ISampleSource? TrackVoice;
    }
}
