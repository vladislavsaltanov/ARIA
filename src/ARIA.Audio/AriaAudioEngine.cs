namespace Aria.Audio;

using System.Collections.Concurrent;
using Aria.Core.Playback;

public sealed class AriaAudioEngine : IAudioEngine, IDisposable
{
    private readonly MixerBus _mixer;
    private readonly IAudioSink _sink;
    private readonly ISourceFactory _sourceFactory;
    private readonly PlaybackMonitor? _monitor;
    private readonly MeterMonitor? _meters;
    private readonly LufsMeter _lufs;
    private readonly float[] _block;
    private readonly Thread _renderThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, StreamHandle> _mixerHandles = new();
    private readonly ConcurrentQueue<int> _faultedAtBirth = [];
    private long _lastPublishTicks = Environment.TickCount64 - 1000;
    private long _lastMeterTicks = Environment.TickCount64 - 1000;
    private long _masterGainBits = BitConverter.DoubleToInt64Bits(1.0);
    private int _handleCounter;
    private int _currentHandle;

    public AriaAudioEngine(
        ISourceFactory sourceFactory,
        PlaybackMonitor? monitor = null,
        int sampleRate = 48000,
        int channels = 2,
        int blockSizeFrames = 512,
        IAudioSink? sink = null,
        MeterMonitor? meters = null)
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
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "aria-render" };
        _renderThread.Start();
    }

    public event Action<StreamEvent>? Events;

    public StreamHandle StartStream(TrackSource source, StreamOptions options)
    {
        var handle = new StreamHandle(Interlocked.Increment(ref _handleCounter));
        var sample = _sourceFactory.Open(source.FilePath, source.CueIn, source.CueOut);
        if (sample is null)
        {
            _faultedAtBirth.Enqueue(handle.Value);
            return handle;
        }
        var mixerHandle = _mixer.AddVoice(new VoiceConfig(sample, 0.0, null, null, options.Markers, source.CueIn, source.CueOut));
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

    public void SetMasterGain(double gainDb)
        => Volatile.Write(ref _masterGainBits, BitConverter.DoubleToInt64Bits(Math.Pow(10.0, gainDb / 20.0)));

    public void Panic(PanicSpec spec)
    {
        _mixer.StopAll(spec.FadeDuration);
        Volatile.Write(ref _currentHandle, 0);
        _sink.Flush();
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
        _cts.Dispose();
    }

    private void ForwardEvent(StreamEvent e) => Events?.Invoke(e);

    private void RenderLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            EmitFaults();
            _mixer.Render(_block);
            ApplyMasterGain();
            PublishMeter();
            var accepted = _sink.Write(_block);
            if (accepted < _block.Length)
            {
                Thread.Sleep(1);
            }
            PublishPosition();
        }
    }

    private void EmitFaults()
    {
        while (_faultedAtBirth.TryDequeue(out var handle))
        {
            Events?.Invoke(new StreamEvent(new StreamHandle(handle), StreamEventKind.Faulted, StreamEndReason.Faulted));
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
        if (now - Volatile.Read(ref _lastMeterTicks) < 100)
        {
            return;
        }
        Volatile.Write(ref _lastMeterTicks, now);
        _meters.Publish(_lufs.MomentaryLufs);
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
