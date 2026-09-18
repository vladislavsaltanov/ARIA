namespace Aria.Audio;

using System.Buffers;
using System.Collections.Concurrent;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class AriaAudioEngine : IAudioEngine, IDisposable
{
    private readonly MixerBus _mixer;
    private IAudioSink _sink;
    private readonly ISourceFactory _sourceFactory;
    private readonly PlaybackMonitor? _monitor;
    private readonly MeterMonitor? _meters;
    private readonly LufsMeter _lufs;
    private readonly float[] _block;
    private readonly Thread _renderThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, StreamHandle> _mixerHandles = new();
    private readonly ConcurrentQueue<(IAudioSink Sink, bool Preview)> _pendingSinks = [];
    private readonly ConcurrentQueue<BirthFault> _faultedAtBirth = [];
    private readonly MixerBus _preview;
    private IAudioSink _previewSink;
    private readonly float[] _previewBlock;
    private readonly ConcurrentDictionary<int, StreamHandle> _previewHandles = new();
    private long _previewGainBits = BitConverter.DoubleToInt64Bits(1.0);
    private int _previewMuted;
    private long _lastPublishTicks = Environment.TickCount64 - 1000;
    private long _lastMeterTicks = Environment.TickCount64 - 1000;
    private long _masterGainBits = BitConverter.DoubleToInt64Bits(1.0);
    private GlobalAudioSettings _globalAudio = GlobalAudioSettings.Default;
    private readonly HighPassNode _masterHpf;
    private readonly SevenBandEq _masterEq;
    private readonly PanNode _masterPan;
    private readonly SimpleLimiter _masterLimiter;
    private readonly MonoSumNode _masterMono;
    private readonly PreviewTap? _previewTap;
    private readonly PreviewTap _mainTap;
    private bool _mainPlaying;
    private readonly int _blockFrames;
    private readonly int _sampleRate;
    private readonly int _channels;
    private PreviewSession[] _sessions = [];
    private readonly float[] _sessionScratch;
    private readonly float[] _clickScratch;
    private readonly object _sessionLock = new();
    private readonly ConcurrentQueue<ISampleSource> _retiredVoices = [];

    private static readonly ClickSettings DefaultClick = new(120, 4, 0, 0);
    private int _handleCounter;
    private int _currentHandle;

    public AriaAudioEngine(
        ISourceFactory sourceFactory,
        PlaybackMonitor? monitor = null,
        int sampleRate = 48000,
        int channels = 2,
        int blockSizeFrames = 512,
        IAudioSink? sink = null,
        MeterMonitor? meters = null,
        IAudioSink? previewSink = null,
        PreviewTap? previewTap = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        _sourceFactory = sourceFactory;
        _monitor = monitor;
        _meters = meters;
        _lufs = new LufsMeter(channels, sampleRate, TimeSpan.FromMilliseconds(400));
        _sink = sink ?? new NullSink(sampleRate, channels);
        _mixer = new MixerBus(channels, sampleRate, blockSizeFrames);
        _block = new float[blockSizeFrames * channels];
        _mixer.Events += ForwardEvent;
        _mixer.Events += OnMixerEnded;
        _masterHpf = new HighPassNode(0, channels, sampleRate);
        _masterEq = new SevenBandEq(AudioChain.SafeFrequencies(GlobalAudioSettings.Default.Eq.Bands, sampleRate), channels, sampleRate);
        _masterPan = new PanNode(0);
        _masterLimiter = new SimpleLimiter(channels, sampleRate);
        _masterMono = new MonoSumNode();
        _previewTap = previewTap;
        _mainTap = new PreviewTap(sampleRate * 2, channels);
        _blockFrames = blockSizeFrames;
        _sampleRate = sampleRate;
        _channels = channels;
        _sessionScratch = new float[blockSizeFrames * channels];
        _clickScratch = new float[blockSizeFrames * channels];
        _previewSink = previewSink ?? new NullSink(sampleRate, channels);
        _preview = new MixerBus(channels, sampleRate, blockSizeFrames);
        _previewBlock = new float[blockSizeFrames * channels];
        _preview.Events += ForwardEvent;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "aria-render" };
        _renderThread.Start();
    }

    public event Action<StreamEvent>? Events;

    public StreamHandle StartStream(TrackSource source, StreamOptions options)
    {
        var handle = new StreamHandle(Interlocked.Increment(ref _handleCounter));
        if (!_sourceFactory.TryOpen(source.FilePath, source.CueIn, source.CueOut, out var sample, out var fault) || sample is null)
        {
            _faultedAtBirth.Enqueue(new BirthFault(handle.Value, fault));
            return handle;
        }
        var mixerHandle = _mixer.AddVoice(new VoiceConfig(sample, 0.0, null, null, options.Markers, source.CueIn, source.CueOut, ResolveAudio(source.Audio), StartInactive: true));
        _mixerHandles[handle.Value] = mixerHandle;
        Volatile.Write(ref _currentHandle, handle.Value);
        return handle;
    }

    public void Transport(StreamHandle handle, TransportCommand command)
    {
        if (_mixerHandles.TryGetValue(handle.Value, out var mixerHandle))
        {
            if (command == TransportCommand.Play)
            {
                Volatile.Write(ref _mainPlaying, true);
                if (MixerPositionFrames(mixerHandle) <= _blockFrames)
                {
                    ResetSessionVoices();
                }
            }
            else if (command == TransportCommand.Pause || command == TransportCommand.Stop)
            {
                Volatile.Write(ref _mainPlaying, false);
            }
            _mixer.Transport(mixerHandle, command);
        }
    }

    public void SetMix(StreamHandle handle, MixParameters mix)
    {
        if (_mixerHandles.TryGetValue(handle.Value, out var mixerHandle))
        {
            _mixer.SetMix(mixerHandle, mix);
        }
    }

    public void Seek(StreamHandle handle, TimeSpan position)
    {
        if (_mixerHandles.TryGetValue(handle.Value, out var mixerHandle))
        {
            var frames = (long)Math.Round(position.TotalSeconds * _mixer.SampleRate);
            _mixer.Seek(mixerHandle, Math.Max(0, frames));
            Volatile.Read(ref _sink).Flush();
        }
    }

    public void SetMasterGain(double gainDb)
        => Volatile.Write(ref _masterGainBits, BitConverter.DoubleToInt64Bits(Math.Pow(10.0, gainDb / 20.0)));

    public void SetSmoothing(Smoothing smoothing)
    {
        ArgumentNullException.ThrowIfNull(smoothing);
        _mixer.SetSmoothing(smoothing.StopFade, smoothing.StartFade, smoothing.Enabled);
    }

    public void Panic(PanicSpec spec)
    {
        Volatile.Write(ref _mainPlaying, false);
        _mixer.StopAll(spec.FadeDuration);
        _preview.StopAll(spec.FadeDuration);
        _previewHandles.Clear();
        Volatile.Write(ref _currentHandle, 0);
        Volatile.Read(ref _sink).Flush();
    }

    public StreamHandle StartPreview(TrackSource source, StreamOptions options)
    {
        var handle = new StreamHandle(Interlocked.Increment(ref _handleCounter));
        if (!_sourceFactory.TryOpen(source.FilePath, source.CueIn, source.CueOut, out var sample, out var fault) || sample is null)
        {
            _faultedAtBirth.Enqueue(new BirthFault(handle.Value, fault));
            return handle;
        }
        _preview.StopAll(TimeSpan.Zero);
        _previewHandles.Clear();
        var mixerHandle = _preview.AddVoice(new VoiceConfig(sample, 0.0, null, null, options.Markers, source.CueIn, source.CueOut, ResolveAudio(source.Audio)));
        _previewHandles[handle.Value] = mixerHandle;
        return handle;
    }

    public void StopPreview()
    {
        _preview.StopAll(TimeSpan.Zero);
        _previewHandles.Clear();
        Volatile.Read(ref _previewSink).Flush();
    }

    public void SetPreviewGain(double gainDb)
        => Volatile.Write(ref _previewGainBits, BitConverter.DoubleToInt64Bits(Math.Pow(10.0, gainDb / 20.0)));

    public void SetPreviewMuted(bool muted)
        => Volatile.Write(ref _previewMuted, muted ? 1 : 0);

    public void ReplaceSink(IAudioSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _pendingSinks.Enqueue((sink, false));
    }

    public void ReplacePreviewSink(IAudioSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _pendingSinks.Enqueue((sink, true));
    }

    public void SetVoiceAudio(StreamHandle handle, TrackAudioSettings audio)
    {
        if (_mixerHandles.TryGetValue(handle.Value, out var mixerHandle))
        {
            _mixer.SetVoiceAudio(mixerHandle, ResolveAudio(audio));
        }
    }

    private TrackAudioSettings ResolveAudio(TrackAudioSettings? audio)
    {
        var resolved = audio ?? TrackAudioSettings.Default;
        var global = Volatile.Read(ref _globalAudio);
        if (global is null || !global.NormalizeEnabled || !resolved.NormalizeEnabled || resolved.MeasuredLufs is not { } measured)
        {
            return resolved;
        }
        var target = resolved.NormalizeTargetLufs ?? global.NormalizeTargetLufs;
        return resolved with { GainDb = LufsNormalize.AdjustGain(resolved.GainDb, measured, target) };
    }

    public void SetGlobalAudio(GlobalAudioSettings audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        Volatile.Write(ref _globalAudio, audio);
    }

    public double ScanTrackLufs(string filePath)
    {
        try
        {
            var source = _sourceFactory.Open(filePath, TimeSpan.Zero, null);
            if (source is null || source.Channels <= 0 || source.SampleRate <= 0)
            {
                (source as IDisposable)?.Dispose();
                return double.NaN;
            }
            using (source as IDisposable)
            {
                return MeasureIntegratedLufs(source);
            }
        }
        catch
        {
            return double.NaN;
        }
    }

    private static double MeasureIntegratedLufs(ISampleSource source)
    {
        var scan = new IntegratedLufsScan(source.Channels, source.SampleRate);
        var buffer = ArrayPool<float>.Shared.Rent(Math.Min(65536, 4096 * source.Channels));
        try
        {
            int read;
            while ((read = source.ReadFrames(buffer.AsSpan(0, buffer.Length / source.Channels * source.Channels))) > 0)
            {
                scan.Feed(buffer.AsSpan(0, read * source.Channels), source.Channels);
            }
            return scan.Result;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }

    public void DisposeStream(StreamHandle handle)
    {
        if (_mixerHandles.TryRemove(handle.Value, out var mixerHandle))
        {
            _mixer.RemoveVoice(mixerHandle);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _renderThread.Join(TimeSpan.FromSeconds(2));
        _mixer.Dispose();
        _preview.Dispose();
        (_sink as IDisposable)?.Dispose();
        (_previewSink as IDisposable)?.Dispose();
        _cts.Dispose();
    }

    private sealed record BirthFault(int Handle, SourceOpenFault Cause);

    private void ForwardEvent(StreamEvent e) => Events?.Invoke(e);

    private void OnMixerEnded(StreamEvent e)
    {
        if (e.Kind == StreamEventKind.Ended)
        {
            Volatile.Write(ref _mainPlaying, false);
        }
    }

    private long MixerPositionFrames(StreamHandle mixerHandle) =>
        _mixer.TryGetPosition(mixerHandle, out var position)
            ? (long)(position.TotalSeconds * _mixer.SampleRate)
            : long.MaxValue;

    private void RenderLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            DrainPendingSinks();
            EmitFaults();
            _mixer.Render(_block);
            ApplyMasterChain();
            PublishMeter();
            var accepted = _sink.Write(_block);
            if (accepted < _block.Length)
            {
                Thread.Sleep(1);
            }
            RenderPreview();
            PublishPosition();
        }
    }

    private void RenderPreview()
    {
        _mainTap.Publish(_block);
        _preview.Render(_previewBlock);
        if (Volatile.Read(ref _previewMuted) == 1)
        {
            Array.Clear(_previewBlock);
        }
        else
        {
            var gain = (float)BitConverter.Int64BitsToDouble(Volatile.Read(ref _previewGainBits));
            if (gain != 1f)
            {
                for (var index = 0; index < _previewBlock.Length; index++)
                {
                    _previewBlock[index] *= gain;
                }
            }
        }
        _previewSink.Write(_previewBlock);
        _previewTap?.Publish(_previewBlock);
        PumpPreviewSessions();
    }

    public PreviewSessionHandle OpenPreviewSession()
    {
        var handle = new PreviewSessionHandle(Interlocked.Increment(ref _handleCounter));
        var tap = new PreviewTap(Math.Max(256, _sampleRate * 2), _channels);
        var voice = new ClickVoice(_channels, _sampleRate, DefaultClick);
        var session = new PreviewSession(handle, _mainTap.Subscribe(), voice, tap);
        lock (_sessionLock)
        {
            var grown = new PreviewSession[_sessions.Length + 1];
            Array.Copy(_sessions, grown, _sessions.Length);
            grown[_sessions.Length] = session;
            _sessions = grown;
        }
        return handle;
    }

    public void ClosePreviewSession(PreviewSessionHandle session)
    {
        var target = FindSession(session);
        if (target?.TrackVoice is { } voice)
        {
            _retiredVoices.Enqueue(voice);
            target.TrackVoice = null;
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

    public void StartSessionTrack(PreviewSessionHandle session, TrackSource source)
    {
        if (FindSession(session) is not { } target)
        {
            return;
        }
        if (!_sourceFactory.TryOpen(source.FilePath, source.CueIn, source.CueOut, out var sample, out var fault) || sample is null)
        {
            _faultedAtBirth.Enqueue(new BirthFault(session.Value, fault));
            return;
        }
        var old = Volatile.Read(ref target.TrackVoice);
        Volatile.Write(ref target.TrackVoice, sample);
        if (old is not null)
        {
            _retiredVoices.Enqueue(old);
        }
        target.ResetVoice = true;
    }

    public void SetClick(PreviewSessionHandle session, ClickSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (FindSession(session) is { } target)
        {
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

    public PreviewTap? PreviewSessionTap(PreviewSessionHandle session) => FindSession(session)?.Tap;

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

    private void ResetSessionVoices()
    {
        foreach (var session in Volatile.Read(ref _sessions))
        {
            session.ResetVoice = true;
        }
    }

    private void PumpPreviewSessions()
    {
        while (_retiredVoices.TryDequeue(out var retired))
        {
            (retired as IDisposable)?.Dispose();
        }
        var sessions = Volatile.Read(ref _sessions);
        if (sessions.Length == 0)
        {
            return;
        }
        foreach (var session in sessions)
        {
            PumpSession(session);
        }
    }

    private void PumpSession(PreviewSession session)
    {
        var voice = Volatile.Read(ref session.TrackVoice);
        if (voice is null && !Volatile.Read(ref _mainPlaying))
        {
            return;
        }
        if (session.Reader is null && voice is null)
        {
            return;
        }
        if (session.ResetVoice)
        {
            session.Voice.Seek(0);
            session.ResetVoice = false;
        }
        if (session.PendingSettings is { } settings)
        {
            session.Voice.UpdateSettings(settings);
            session.PendingSettings = null;
        }
        var read = voice is not null
            ? voice.ReadFrames(_sessionScratch.AsSpan(0, _sessionScratch.Length)) * _channels
            : session.Reader!.Read(_sessionScratch.AsSpan(0, _sessionScratch.Length));
        if (read <= 0)
        {
            return;
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

    private sealed class PreviewSession
    {
        public PreviewSession(PreviewSessionHandle handle, PreviewReader? reader, ClickVoice voice, PreviewTap tap)
        {
            Handle = handle;
            Reader = reader;
            Voice = voice;
            Tap = tap;
        }

        public PreviewSessionHandle Handle { get; }

        public PreviewReader? Reader { get; }

        public ClickVoice Voice { get; }

        public PreviewTap Tap { get; }

        public volatile bool ClickMuted = true;

        public volatile ClickSettings? PendingSettings;

        public volatile bool ResetVoice;

        public ISampleSource? TrackVoice;
    }

    private void DrainPendingSinks()
    {
        while (_pendingSinks.TryDequeue(out var pending))
        {
            if (pending.Preview)
            {
                var old = Interlocked.Exchange(ref _previewSink, pending.Sink);
                (old as IDisposable)?.Dispose();
            }
            else
            {
                var old = Interlocked.Exchange(ref _sink, pending.Sink);
                (old as IDisposable)?.Dispose();
            }
        }
    }

    private void EmitFaults()
    {
        while (_faultedAtBirth.TryDequeue(out var birth))
        {
            var detail = birth.Cause == SourceOpenFault.Unknown ? null : birth.Cause.ToString();
            Events?.Invoke(new StreamEvent(new StreamHandle(birth.Handle), StreamEventKind.Faulted, StreamEndReason.Faulted, detail));
        }
    }

    private void ApplyMasterChain()
    {
        var global = Volatile.Read(ref _globalAudio);
        var span = _block.AsSpan();
        _masterHpf.SetFrequency(global.HpfHz);
        _masterHpf.Process(span, _mixer.Channels);
        AudioChain.ApplyEq(_masterEq, global.Eq);
        _masterEq.Process(span, _mixer.Channels);
        if (_mixer.Channels == 2 && global.Pan != 0.0)
        {
            _masterPan.SetPan(global.Pan);
            _masterPan.Process(span, _mixer.Channels);
        }
        ApplyMasterGain();
        if (global.Limiter.Enabled)
        {
            _masterLimiter.SetParams(global.Limiter.ThresholdDb, global.Limiter.ReleaseMs);
            _masterLimiter.Process(span, _mixer.Channels);
        }
        if (global.Mono && _mixer.Channels == 2)
        {
            _masterMono.Enabled = true;
            _masterMono.Process(span, _mixer.Channels);
        }
        else
        {
            _masterMono.Enabled = false;
        }
    }

    private void ApplyMasterGain()
    {
        var gain = (float)BitConverter.Int64BitsToDouble(Volatile.Read(ref _masterGainBits));
        if (gain == 1f)
        {
            return;
        }
        for (var index = 0; index < _block.Length; index++)
        {
            _block[index] *= gain;
        }
    }

    private void PublishMeter()
    {
        if (_meters is null)
        {
            return;
        }
        _lufs.Process(_block.AsSpan(), _mixer.Channels);
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastMeterTicks) < 33)
        {
            return;
        }
        Volatile.Write(ref _lastMeterTicks, now);
        _meters.Publish(_lufs.MomentaryLufs, _mixer.PeakLeft, _mixer.PeakRight);
    }

    private void PublishPosition()
    {
        if (_monitor is null)
        {
            return;
        }
        var current = Volatile.Read(ref _currentHandle);
        if (current == 0)
        {
            return;
        }
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastPublishTicks) < 10)
        {
            return;
        }
        if (!_mixerHandles.TryGetValue(current, out var mixerHandle) || !_mixer.TryGetPosition(mixerHandle, out var position))
        {
            return;
        }
        Volatile.Write(ref _lastPublishTicks, now);
        _monitor.Publish(new StreamHandle(current), position);
    }
}
