namespace Aria.Audio;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

internal static class AudioChain
{
    public static float[] SafeFrequencies(ImmutableArray<EqBand> bands, int sampleRate)
    {
        var ceiling = sampleRate * 0.45f;
        var result = new float[SevenBandEq.BandCount];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = Math.Min(bands[index].FrequencyHz, ceiling);
        }
        return result;
    }

    public static SevenBandEq BuildEq(TrackAudioSettings audio, int channels, int sampleRate)
    {
        var eq = new SevenBandEq(SafeFrequencies(audio.Eq.Bands, sampleRate), channels, sampleRate);
        ApplyEq(eq, audio);
        return eq;
    }

    public static void ApplyEq(SevenBandEq eq, TrackAudioSettings audio) => ApplyEq(eq, audio.Eq);

    public static void ApplyEq(SevenBandEq eq, AudioEq audioEq)
    {
        for (var band = 0; band < SevenBandEq.BandCount; band++)
        {
            eq.SetBand(band, audioEq.Bands[band].GainDb, audioEq.Bands[band].Q);
        }
    }
}

public sealed record VoiceConfig(
    ISampleSource Source,
    double GainDb,
    Fade? FadeIn,
    Fade? Out,
    ImmutableArray<MarkerSpec> Markers,
    TimeSpan CueIn,
    TimeSpan? CueOut,
    TrackAudioSettings? Audio = null,
    bool StartInactive = false);

public sealed class MixerBus : IDisposable
{
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _blockSizeFrames;
    private readonly ConcurrentQueue<MixerCommand> _commands = new();
    private readonly List<MixerVoice> _voices = [];
    private long _handleCounter;
    private int _pauseFadeFrames;
    private int _resumeFadeFrames;
    private int _smoothingEnabled;

    public MixerBus(int channels, int sampleRate, int blockSizeFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSizeFrames);
        _channels = channels;
        _sampleRate = sampleRate;
        _blockSizeFrames = blockSizeFrames;
    }

    public int Channels => _channels;

    public int SampleRate => _sampleRate;

    public float Peak { get; private set; }

    public event Action<StreamEvent>? Events;

    public StreamHandle AddVoice(VoiceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(config.Source);
        if (config.Source.Channels != _channels)
        {
            throw new ArgumentException("Source channel count does not match the mixer.", nameof(config));
        }
        var handle = new StreamHandle((int)Interlocked.Increment(ref _handleCounter));
        _commands.Enqueue(new MixerCommand(CommandKind.Add, CreateVoice(handle, config), handle, default, null, default));
        return handle;
    }

    public bool TryGetPosition(StreamHandle handle, out TimeSpan position)
    {
        for (var index = 0; index < _voices.Count; index++)
        {
            var voice = _voices[index];
            if (voice.Handle.Value == handle.Value && !voice.Dead && !voice.RemoveRequested)
            {
                position = TimeSpan.FromSeconds(Volatile.Read(ref voice.StartFrame) / (double)_sampleRate);
                return true;
            }
        }
        position = TimeSpan.Zero;
        return false;
    }

    public void StopAll(TimeSpan fadeDuration)
        => _commands.Enqueue(new MixerCommand(CommandKind.StopAll, null, default, default, null, fadeDuration));

    public void Transport(StreamHandle handle, TransportCommand command)
        => _commands.Enqueue(new MixerCommand(CommandKind.Transport, null, handle, command, null, default));

    public void SetMix(StreamHandle handle, MixParameters mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        _commands.Enqueue(new MixerCommand(CommandKind.SetMix, null, handle, default, mix, default));
    }

    public void SetVoiceAudio(StreamHandle handle, TrackAudioSettings audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _commands.Enqueue(new MixerCommand(CommandKind.SetAudio, null, handle, default, null, default, 0, audio));
    }

    public void RemoveVoice(StreamHandle handle)
        => _commands.Enqueue(new MixerCommand(CommandKind.Remove, null, handle, default, null, default));

    public void Seek(StreamHandle handle, long frameIndex)
        => _commands.Enqueue(new MixerCommand(CommandKind.Seek, null, handle, default, null, default, frameIndex));

    public void SetSmoothing(TimeSpan pauseFade, TimeSpan resumeFade, bool enabled)
    {
        var pauseFrames = enabled && pauseFade > TimeSpan.Zero
            ? Math.Max(1, (int)Math.Round(pauseFade.TotalSeconds * _sampleRate))
            : 0;
        var resumeFrames = enabled && resumeFade > TimeSpan.Zero
            ? Math.Max(1, (int)Math.Round(resumeFade.TotalSeconds * _sampleRate))
            : 0;
        Volatile.Write(ref _pauseFadeFrames, pauseFrames);
        Volatile.Write(ref _resumeFadeFrames, resumeFrames);
        Volatile.Write(ref _smoothingEnabled, enabled ? 1 : 0);
    }

    public int Render(Span<float> output)
    {
        if (output.Length % _channels != 0)
        {
            throw new ArgumentException("Output length must be a multiple of the mixer channel count.", nameof(output));
        }
        var totalFrames = output.Length / _channels;
        if (totalFrames == 0)
        {
            Peak = 0f;
            return 0;
        }
        // Drain control queue first: audio thread never blocks on producers.
        DrainCommands();
        output.Clear();
        var processed = 0;
        while (processed < totalFrames)
        {
            var frames = Math.Min(_blockSizeFrames, totalFrames - processed);
            RenderChunk(output.Slice(processed * _channels, frames * _channels), frames);
            processed += frames;
            Compact();
        }
        ComputePeak(output);
        return totalFrames;
    }

    public void Dispose()
    {
        for (var index = 0; index < _voices.Count; index++)
        {
            CloseSource(_voices[index]);
        }
        _voices.Clear();
        while (_commands.TryDequeue(out _))
        {
        }
    }

    private MixerVoice CreateVoice(StreamHandle handle, VoiceConfig config)
    {
        var markers = config.Markers.IsDefaultOrEmpty ? Array.Empty<MarkerSpec>() : config.Markers.ToArray();
        if (markers.Length > 1)
        {
            Array.Sort(markers, static (left, right) => left.Position.CompareTo(right.Position));
        }
        var fadeIn = config.FadeIn;
        var fadeInFrames = fadeIn is null || fadeIn.Duration <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (int)Math.Round(fadeIn.Duration.TotalSeconds * _sampleRate));
        return new MixerVoice
        {
            Handle = handle,
            Source = config.Source,
            Scratch = new float[_blockSizeFrames * _channels],
            Fader = new FaderNode(fadeInFrames, fadeIn?.Curve ?? FadeCurve.Linear, 0.0, 1.0, stopWhenDone: false),
            Gain = new GainNode(config.GainDb + (config.Audio?.GainDb ?? 0.0)),
            BaseGainDb = config.GainDb,
            Eq = AudioChain.BuildEq(config.Audio ?? TrackAudioSettings.Default, _channels, _sampleRate),
            Pan = new PanNode(config.Audio?.Pan ?? 0.0),
            BypassPan = (config.Audio?.Pan ?? 0.0) == 0.0,
            Markers = markers,
            CueInSeconds = config.CueIn.TotalSeconds,
            HasCueOut = config.CueOut is not null,
            CueOutSeconds = config.CueOut?.TotalSeconds ?? 0.0,
            Active = !config.StartInactive,
        };
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case CommandKind.Add:
                    _voices.Add(command.Voice!);
                    break;
                case CommandKind.Remove:
                    var removed = FindVoice(command.Handle);
                    if (removed is not null)
                    {
                        removed.RemoveRequested = true;
                    }
                    break;
                case CommandKind.Transport:
                    ApplyTransport(FindVoice(command.Handle), command.Transport);
                    break;
                case CommandKind.SetMix:
                    ApplyMix(FindVoice(command.Handle), command.Mix!);
                    break;
                case CommandKind.SetAudio:
                    ApplyAudio(FindVoice(command.Handle), command.Audio!);
                    break;
                case CommandKind.Seek:
                    ApplySeek(FindVoice(command.Handle), command.SeekFrame);
                    break;
                case CommandKind.StopAll:
                    ApplyStopAll(command.Duration);
                    break;
            }
        }
    }

    private void ApplyStopAll(TimeSpan fadeDuration)
    {
        for (var index = 0; index < _voices.Count; index++)
        {
            var voice = _voices[index];
            if (voice.Dead || voice.RemoveRequested)
            {
                continue;
            }
            if (fadeDuration > TimeSpan.Zero)
            {
                var rampFrames = Math.Max(1, (int)Math.Round(fadeDuration.TotalSeconds * _sampleRate));
                voice.Fader = new FaderNode(rampFrames, FadeCurve.Linear, voice.Fader.Level, 0.0, stopWhenDone: true);
            }
            else
            {
                EndVoice(voice, StreamEndReason.StoppedByCommand);
            }
        }
    }

    private void ApplyTransport(MixerVoice? voice, TransportCommand command)
    {
        if (voice is null || voice.RemoveRequested)
        {
            return;
        }
        switch (command)
        {
            case TransportCommand.Play:
                if (!voice.Active || voice.PauseWhenFaded)
                {
                    voice.PauseWhenFaded = false;
                    var resumeFrames = Volatile.Read(ref _resumeFadeFrames);
                    if (Volatile.Read(ref _smoothingEnabled) == 1 && resumeFrames > 0)
                    {
                        voice.Fader = new FaderNode(resumeFrames, FadeCurve.Linear, 0.0, 1.0, stopWhenDone: false);
                    }
                    voice.Active = true;
                }
                break;
            case TransportCommand.Pause:
                if (voice.Dead)
                {
                    break;
                }
                if (!voice.Active || voice.PauseWhenFaded)
                {
                    voice.Active = false;
                    voice.PauseWhenFaded = false;
                    break;
                }
                var pauseFrames = Volatile.Read(ref _pauseFadeFrames);
                if (Volatile.Read(ref _smoothingEnabled) == 1 && pauseFrames > 0)
                {
                    voice.Fader = new FaderNode(pauseFrames, FadeCurve.Linear, voice.Fader.Level, 0.0, stopWhenDone: false);
                    voice.PauseWhenFaded = true;
                }
                else
                {
                    voice.Active = false;
                }
                break;
            case TransportCommand.Stop:
                voice.PauseWhenFaded = false;
                EndVoice(voice, StreamEndReason.StoppedByCommand);
                break;
        }
    }

    private static void ApplySeek(MixerVoice? voice, long frameIndex)
    {
        if (voice is null || voice.RemoveRequested || voice.Dead)
        {
            return;
        }
        voice.Source.Seek(frameIndex);
        voice.StartFrame = frameIndex;
    }

    private void ApplyMix(MixerVoice? voice, MixParameters mix)
    {
        if (voice is null || voice.RemoveRequested)
        {
            return;
        }
        if (mix.Fade is not { } fade)
        {
            return;
        }
        var rampFrames = fade.Duration <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (int)Math.Round(fade.Duration.TotalSeconds * _sampleRate));
        var target = Math.Pow(10.0, fade.TargetDb / 20.0);
        var from = fade.StopWhenDone ? voice.Fader.Level : 0.0;
        voice.Fader = new FaderNode(rampFrames, fade.Curve, from, target, fade.StopWhenDone);
        if (rampFrames == 0 && fade.StopWhenDone)
        {
            EndVoice(voice, StreamEndReason.FadeCompleted);
        }
    }

    private void ApplyAudio(MixerVoice? voice, TrackAudioSettings audio)
    {
        if (voice is null || voice.RemoveRequested)
        {
            return;
        }
        voice.Eq = AudioChain.BuildEq(audio, _channels, _sampleRate);
        voice.Pan = new PanNode(audio.Pan);
        voice.BypassPan = audio.Pan == 0.0;
        voice.Gain.SetGainDb(voice.BaseGainDb + audio.GainDb);
    }

    private void RenderChunk(Span<float> chunk, int frames)
    {
        for (var index = 0; index < _voices.Count; index++)
        {
            var voice = _voices[index];
            if (voice.RemoveRequested || voice.Dead || !voice.Active)
            {
                continue;
            }
            var scratch = voice.Scratch.AsSpan(0, frames * _channels);
            int read;
            try
            {
                read = voice.Source.ReadFrames(scratch);
            }
            catch
            {
                FailVoice(voice);
                continue;
            }
            var validSamples = read * _channels;
            var fader = voice.Fader;
            var wasCompleted = fader.HasCompleted;
            if (validSamples > 0)
            {
                var segment = scratch.Slice(0, validSamples);
                voice.Eq.Process(segment, _channels);
                voice.Gain.Process(segment, _channels);
                if (_channels == 2 && !voice.BypassPan)
                {
                    voice.Pan.Process(segment, _channels);
                }
                fader.Process(segment, _channels);
            }
            var contributionFrames = read;
            StreamEndReason? reason = null;
            if (fader.StopWhenDone && !wasCompleted && fader.HasCompleted)
            {
                reason = StreamEndReason.FadeCompleted;
            }
            else
            {
                var cutoff = ScanCutoff(voice, read, out var scanReason);
                if (cutoff >= 0)
                {
                    contributionFrames = cutoff;
                    reason = scanReason;
                    scratch.Slice(cutoff * _channels, validSamples - cutoff * _channels).Clear();
                }
                else if (read < frames)
                {
                    reason = StreamEndReason.Completed;
                }
            }
            if (!reason.HasValue && voice.PauseWhenFaded && fader.HasCompleted)
            {
                voice.Active = false;
                voice.PauseWhenFaded = false;
            }
            if (reason.HasValue)
            {
                EndVoice(voice, reason.Value);
            }
            var contributionSamples = contributionFrames * _channels;
            for (var sample = 0; sample < contributionSamples; sample++)
            {
                chunk[sample] += scratch[sample];
            }
            voice.StartFrame += read;
        }
    }

    private int ScanCutoff(MixerVoice voice, int frames, out StreamEndReason reason)
    {
        reason = default;
        var markers = voice.Markers;
        var hasMarker = markers.Length > 0;
        var hasCueOut = voice.HasCueOut;
        if (!hasMarker && !hasCueOut)
        {
            return -1;
        }
        var markerPos = hasMarker ? markers[0].Position.TotalSeconds : 0.0;
        var cueOutPos = voice.CueOutSeconds;
        var cueIn = voice.CueInSeconds;
        var start = voice.StartFrame;
        for (var frame = 0; frame < frames; frame++)
        {
            var filePos = cueIn + (start + frame) / (double)_sampleRate;
            if (hasMarker && filePos >= markerPos)
            {
                reason = StreamEndReason.StoppedByMarker;
                return frame;
            }
            if (hasCueOut && filePos >= cueOutPos)
            {
                reason = StreamEndReason.CueOutReached;
                return frame;
            }
        }
        return -1;
    }

    private void EndVoice(MixerVoice voice, StreamEndReason reason)
    {
        if (voice.Dead)
        {
            return;
        }
        voice.Dead = true;
        Events?.Invoke(new StreamEvent(voice.Handle, StreamEventKind.Ended, reason));
    }

    private void FailVoice(MixerVoice voice)
    {
        if (voice.Dead)
        {
            return;
        }
        voice.Dead = true;
        Events?.Invoke(new StreamEvent(voice.Handle, StreamEventKind.Faulted, StreamEndReason.Faulted));
    }

    private MixerVoice? FindVoice(StreamHandle handle)
    {
        for (var index = 0; index < _voices.Count; index++)
        {
            if (_voices[index].Handle.Value == handle.Value)
            {
                return _voices[index];
            }
        }
        return null;
    }

    private void Compact()
    {
        var writeIndex = 0;
        for (var index = 0; index < _voices.Count; index++)
        {
            var voice = _voices[index];
            if (!voice.Dead && !voice.RemoveRequested)
            {
                if (writeIndex != index)
                {
                    _voices[writeIndex] = voice;
                }
                writeIndex++;
            }
            else
            {
                CloseSource(voice);
            }
        }
        _voices.RemoveRange(writeIndex, _voices.Count - writeIndex);
    }

    private static void CloseSource(MixerVoice voice)
    {
        try
        {
            (voice.Source as IDisposable)?.Dispose();
        }
        catch
        {
        }
    }

    private void ComputePeak(ReadOnlySpan<float> output)
    {
        float peak = 0;
        for (var index = 0; index < output.Length; index++)
        {
            var magnitude = Math.Abs(output[index]);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }
        Peak = peak;
    }
}
