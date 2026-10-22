namespace Aria.Audio;

using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed class AriaAudioEngine : IAudioEngine, IOutputStampSource, IDisposable
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
    private readonly int _blockFrames;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly TestClickGenerator _testClickGenerator;
    private int _testClick;
    private ClockSlot _clockSlot;
    private int _sinkGeneration;
    private long _sinkWrittenFrames;

    private int _handleCounter;
    private int _currentHandle;
    private int _mainOffset;
    private int _mainPending;
    private long _stallSince;
    private bool _stallReported;
    private long _previewClock;
    private int _consecutiveFaults;
    private readonly long _blockTicks;
    private static readonly long StallThresholdTicks = Stopwatch.Frequency / 2;
    private const int MaxPreviewCatchUpBlocks = 4;

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
        _clockSlot = ClockSlot.For(_sink, 0);
        _mixer = new MixerBus(channels, sampleRate, blockSizeFrames);
        _block = new float[blockSizeFrames * channels];
        _mixer.Events += ForwardEvent;
        _masterHpf = new HighPassNode(0, channels, sampleRate);
        _masterEq = new SevenBandEq(AudioChain.SafeFrequencies(GlobalAudioSettings.Default.Eq.Bands, sampleRate), channels, sampleRate);
        _masterPan = new PanNode(0);
        _masterLimiter = new SimpleLimiter(channels, sampleRate);
        _masterMono = new MonoSumNode();
        _previewTap = previewTap;
        _blockFrames = blockSizeFrames;
        _sampleRate = sampleRate;
        _channels = channels;
        _testClickGenerator = new TestClickGenerator(sampleRate, channels);
        _previewSink = previewSink ?? new NullSink(sampleRate, channels);
        _preview = new MixerBus(channels, sampleRate, blockSizeFrames);
        _previewBlock = new float[blockSizeFrames * channels];
        _preview.Events += ForwardEvent;
        _blockTicks = Math.Max(1, Stopwatch.Frequency * blockSizeFrames / sampleRate);
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "aria-render" };
        _renderThread.Start();
    }

    public event Action<StreamEvent>? Events;

    public MixReportRing Reports { get; } = new();

    public bool TestClickEnabled => Volatile.Read(ref _testClick) == 1;

    public void SetTestClick(bool enabled) => Volatile.Write(ref _testClick, enabled ? 1 : 0);

    public event Action<bool>? MainOutputStalled;

    public event Action<Exception>? RenderFaulted;

    public StreamHandle StartStream(TrackSource source, StreamOptions options)
    {
        var handle = new StreamHandle(Interlocked.Increment(ref _handleCounter));
        if (!_sourceFactory.TryOpen(source.FilePath, source.CueIn, source.CueOut, out var sample, out var fault) || sample is null)
        {
            _faultedAtBirth.Enqueue(new BirthFault(handle.Value, fault));
            return handle;
        }
        var mixerHandle = _mixer.AddVoice(new VoiceConfig(sample, 0.0, null, null, options.Markers, source.CueIn, source.CueOut, ResolveAudio(source.Audio), StartInactive: true, Tag: handle.Value));
        _mixerHandles[handle.Value] = mixerHandle;
        Volatile.Write(ref _currentHandle, handle.Value);
        return handle;
    }

    public void Transport(StreamHandle handle, TransportCommand command)
    {
        if (_mixerHandles.TryGetValue(handle.Value, out var mixerHandle))
        {
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

    public void SetMarkers(StreamHandle handle, ImmutableArray<MarkerSpec> markers)
    {
        if (!_mixerHandles.TryGetValue(handle.Value, out var mixerHandle))
        {
            return;
        }
        var array = markers.IsDefaultOrEmpty ? Array.Empty<MarkerSpec>() : markers.ToArray();
        for (var index = 1; index < array.Length; index++)
        {
            System.Diagnostics.Debug.Assert(array[index - 1].Position <= array[index].Position);
        }
        _mixer.SetMarkers(mixerHandle, array);
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
        if (!_renderThread.Join(TimeSpan.FromSeconds(2)))
        {
            _cts.Dispose();
            return;
        }
        _mixer.Dispose();
        _preview.Dispose();
        (_sink as IDisposable)?.Dispose();
        (_previewSink as IDisposable)?.Dispose();
        _cts.Dispose();
    }

    public int DeviceBufferFrames => Volatile.Read(ref _clockSlot).Source?.DeviceBufferFrames ?? 0;

    public bool TryReadOutputStamp(out OutputStamp stamp)
    {
        var slot = Volatile.Read(ref _clockSlot);
        if (slot.Source is not { } source || slot.Mapping is not { } mapping || !source.TryReadStamp(out var frame, out var nativeNs, out var flushGeneration))
        {
            stamp = default;
            return false;
        }
        stamp = new OutputStamp(frame, mapping.ToHostTicks(nativeNs), flushGeneration, slot.Generation);
        return true;
    }

    private sealed record ClockSlot(IOutputClockSource? Source, NativeClockMapping? Mapping, int Generation)
    {
        public static ClockSlot For(IAudioSink sink, int generation) =>
            sink is IOutputClockSource source
                ? new ClockSlot(source, new NativeClockMapping(source.NativeNowNs), generation)
                : new ClockSlot(null, null, generation);
    }

    private sealed record BirthFault(int Handle, SourceOpenFault Cause);

    private void ForwardEvent(StreamEvent e) => Events?.Invoke(e);

    private void RenderLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                RenderOnce();
                _consecutiveFaults = 0;
            }
            catch (Exception e)
            {
                RecoverFromFault(e);
            }
        }
    }

    private void RenderOnce()
    {
        DrainPendingSinks();
        EmitFaults();
        if (_mainPending == 0)
        {
            _mixer.Render(_block);
            PublishReport();
            ApplyMasterChain();
            PublishMeter();
            _mainOffset = 0;
            _mainPending = _block.Length;
        }
        var accepted = Math.Clamp(_sink.Write(_block.AsSpan(_mainOffset, _mainPending)), 0, _mainPending);
        _sinkWrittenFrames += accepted / _channels;
        _mainOffset += accepted;
        _mainPending -= accepted;
        if (_mainPending == 0)
        {
            ClearStall();
            RenderPreview();
            _previewClock = Stopwatch.GetTimestamp();
            PublishPosition();
            return;
        }
        if (TrackStall(Stopwatch.GetTimestamp()))
        {
            RenderPreviewWhileStalled();
        }
        PublishPosition();
        Thread.Sleep(1);
    }

    private void PublishReport()
    {
        var report = _mixer.LastReport;
        if (Volatile.Read(ref _testClick) == 1)
        {
            foreach (var voice in report)
            {
                if (voice.Active && !voice.Ended)
                {
                    Volatile.Write(ref _testClick, 0);
                    break;
                }
            }
        }
        var testClick = Volatile.Read(ref _testClick) == 1;
        if (testClick)
        {
            _testClickGenerator.Mix(_block, _sinkWrittenFrames);
        }
        Reports.Publish(_sinkWrittenFrames, _sinkGeneration, report, _mixer.LastReportOverflow, testClick);
    }

    private bool TrackStall(long now)
    {
        if (_stallSince == 0)
        {
            _stallSince = now;
            return false;
        }
        if (now - _stallSince < StallThresholdTicks)
        {
            return false;
        }
        if (!_stallReported)
        {
            _stallReported = true;
            _previewClock = now;
            RaiseStall(true);
        }
        return true;
    }

    private void ClearStall()
    {
        _stallSince = 0;
        if (_stallReported)
        {
            _stallReported = false;
            RaiseStall(false);
        }
    }

    private void RaiseStall(bool stalled)
    {
        try
        {
            MainOutputStalled?.Invoke(stalled);
        }
        catch (Exception e)
        {
            RaiseFault(e);
        }
    }

    private void RenderPreviewWhileStalled()
    {
        var now = Stopwatch.GetTimestamp();
        for (var blocks = 0; blocks < MaxPreviewCatchUpBlocks && now - _previewClock >= _blockTicks; blocks++)
        {
            RenderPreview();
            _previewClock += _blockTicks;
        }
        if (now - _previewClock >= _blockTicks)
        {
            _previewClock = now;
        }
    }

    private void RecoverFromFault(Exception e)
    {
        _mainPending = 0;
        Array.Clear(_block);
        _consecutiveFaults = Math.Min(_consecutiveFaults + 1, 20);
        RaiseFault(e);
        Thread.Sleep(5 * _consecutiveFaults);
    }

    private void RaiseFault(Exception e)
    {
        try
        {
            RenderFaulted?.Invoke(e);
        }
        catch (Exception)
        {
        }
    }

    private void RenderPreview()
    {
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
    }

    private void DrainPendingSinks()
    {
        while (_pendingSinks.TryDequeue(out var pending))
        {
            if (pending.Preview)
            {
                var old = Interlocked.Exchange(ref _previewSink, pending.Sink);
                RetireDisposable(old);
            }
            else
            {
                var old = Interlocked.Exchange(ref _sink, pending.Sink);
                _sinkGeneration++;
                _sinkWrittenFrames = 0;
                Volatile.Write(ref _clockSlot, ClockSlot.For(pending.Sink, _sinkGeneration));
                RetireDisposable(old);
                _stallSince = 0;
            }
        }
    }

    private static void RetireDisposable(object? disposable)
    {
        if (disposable is IDisposable target)
        {
            ThreadPool.QueueUserWorkItem(static state => ((IDisposable)state!).Dispose(), target);
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
