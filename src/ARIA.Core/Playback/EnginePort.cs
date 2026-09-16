namespace Aria.Core.Playback;

using System.Collections.Immutable;
using Aria.Core.Model;

public readonly record struct StreamHandle(int Value);

public enum StreamBus
{
    Main,
    Preview,
}

public sealed record TrackSource(string FilePath, TimeSpan CueIn, TimeSpan? CueOut, TrackAudioSettings? Audio = null);

public sealed record MarkerSpec(string Name, TimeSpan Position, MarkerAction Action);

public sealed record StreamOptions(StreamBus Bus, ImmutableArray<MarkerSpec> Markers);

public enum TransportCommand
{
    Play,
    Pause,
    Stop,
}

public sealed record FadeSpec(TimeSpan Duration, FadeCurve Curve, double TargetDb, bool StopWhenDone);

public sealed record MixParameters(double GainDb, FadeSpec? Fade);

public sealed record PanicSpec(TimeSpan FadeDuration);

public enum StreamEndReason
{
    Completed,
    CueOutReached,
    StoppedByMarker,
    FadeCompleted,
    StoppedByCommand,
    Panic,
    Faulted,
    DeviceLost,
}

public enum StreamEventKind
{
    Ended,
    Faulted,
}

public sealed record StreamEvent(StreamHandle Handle, StreamEventKind Kind, StreamEndReason Reason, string? Detail = null);

public enum SourceOpenFault
{
    Unknown,
    Missing,
    Undecodable,
}

public sealed record FaultCause(TrackId Track, SourceOpenFault Cause);

public interface IAudioEngine
{
    StreamHandle StartStream(TrackSource source, StreamOptions options);

    void Transport(StreamHandle handle, TransportCommand command);

    void SetMix(StreamHandle handle, MixParameters mix);

    void Seek(StreamHandle handle, TimeSpan position);

    void SetMasterGain(double gainDb);

    void SetSmoothing(Smoothing smoothing);

    void Panic(PanicSpec spec);

    void DisposeStream(StreamHandle handle);

    StreamHandle StartPreview(TrackSource source, StreamOptions options) => default;

    void StopPreview()
    {
    }

    void SetPreviewGain(double gainDb)
    {
    }

    void SetPreviewMuted(bool muted)
    {
    }

    void SetVoiceAudio(StreamHandle handle, TrackAudioSettings audio)
    {
    }

    void SetGlobalAudio(GlobalAudioSettings audio)
    {
    }

    double ScanTrackLufs(string filePath) => double.NaN;

    event Action<StreamEvent>? Events;
}
