namespace Aria.Audio;

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.Playback;

public sealed record VoiceConfig(
    ISampleSource Source,
    double GainDb,
    Fade? FadeIn,
    Fade? Out,
    ImmutableArray<MarkerSpec> Markers,
    TimeSpan CueIn,
    TimeSpan? CueOut);

public sealed class MixerBus : IDisposable
{
    private enum CommandKind : byte
    {
        Add,
        Remove,
        Transport,
        SetMix,
    }

    private readonly record struct Command(CommandKind Kind, Voice? Voice, StreamHandle Handle, TransportCommand Transport, MixParameters? Mix);

    private sealed class Voice
    {
        public required StreamHandle Handle { get; init; }

        public required ISampleSource Source { get; init; }

        public required float[] Scratch { get; init; }

        public required FaderNode Fader { get; set; }

        public required GainNode Gain { get; init; }

        public required MarkerSpec[] Markers { get; init; }

        public required double CueInSeconds { get; init; }

        public required bool HasCueOut { get; init; }

        public required double CueOutSeconds { get; init; }

        public long StartFrame { get; set; }

        public bool Active { get; set; }

        public bool Dead { get; set; }

        public bool RemoveRequested { get; set; }
    }

    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _blockSizeFrames;
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly List<Voice> _voices = [];
    private long _handleCounter;

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
        _commands.Enqueue(new Command(CommandKind.Add, CreateVoice(handle, config), handle, default, null));
        return handle;
    }

    public void Transport(StreamHandle handle, TransportCommand command)
        => _commands.Enqueue(new Command(CommandKind.Transport, null, handle, command, null));

    public void SetMix(StreamHandle handle, MixParameters mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        _commands.Enqueue(new Command(CommandKind.SetMix, null, handle, default, mix));
    }

    public void RemoveVoice(StreamHandle handle)
        => _commands.Enqueue(new Command(CommandKind.Remove, null, handle, default, null));

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
        _voices.Clear();
        while (_commands.TryDequeue(out _))
        {
        }
    }

    private Voice CreateVoice(StreamHandle handle, VoiceConfig config)
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
        return new Voice
        {
            Handle = handle,
            Source = config.Source,
            Scratch = new float[_blockSizeFrames * _channels],
            Fader = new FaderNode(fadeInFrames, fadeIn?.Curve ?? FadeCurve.Linear, 0.0, 1.0, stopWhenDone: false),
            Gain = new GainNode(config.GainDb),
            Markers = markers,
            CueInSeconds = config.CueIn.TotalSeconds,
            HasCueOut = config.CueOut is not null,
            CueOutSeconds = config.CueOut?.TotalSeconds ?? 0.0,
            Active = true,
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
            }
        }
    }

    private void ApplyTransport(Voice? voice, TransportCommand command)
    {
        if (voice is null || voice.RemoveRequested)
        {
            return;
        }
        switch (command)
        {
            case TransportCommand.Play:
                voice.Active = true;
                break;
            case TransportCommand.Pause:
                if (!voice.Dead)
                {
                    voice.Active = false;
                }
                break;
            case TransportCommand.Stop:
                EndVoice(voice, StreamEndReason.StoppedByCommand);
                break;
        }
    }

    private void ApplyMix(Voice? voice, MixParameters mix)
    {
        if (voice is null || voice.RemoveRequested)
        {
            return;
        }
        voice.Gain.SetGainDb(mix.GainDb);
        if (mix.Fade is not { } fade)
        {
            return;
        }
        var rampFrames = fade.Duration <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (int)Math.Round(fade.Duration.TotalSeconds * _sampleRate));
        voice.Fader = new FaderNode(rampFrames, fade.Curve, 1.0, Math.Pow(10.0, fade.TargetDb / 20.0), fade.StopWhenDone);
        if (rampFrames == 0 && fade.StopWhenDone)
        {
            EndVoice(voice, StreamEndReason.FadeCompleted);
        }
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
            var read = voice.Source.ReadFrames(scratch);
            var validSamples = read * _channels;
            var fader = voice.Fader;
            var wasCompleted = fader.HasCompleted;
            if (validSamples > 0)
            {
                fader.Process(scratch.Slice(0, validSamples), _channels);
                voice.Gain.Process(scratch.Slice(0, validSamples), _channels);
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

    private int ScanCutoff(Voice voice, int frames, out StreamEndReason reason)
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

    private void EndVoice(Voice voice, StreamEndReason reason)
    {
        if (voice.Dead)
        {
            return;
        }
        voice.Dead = true;
        Events?.Invoke(new StreamEvent(voice.Handle, StreamEventKind.Ended, reason));
    }

    private Voice? FindVoice(StreamHandle handle)
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
        }
        _voices.RemoveRange(writeIndex, _voices.Count - writeIndex);
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
