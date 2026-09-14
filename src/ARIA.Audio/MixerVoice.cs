namespace Aria.Audio;

using Aria.Core.Playback;

internal sealed class MixerVoice
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

    public long StartFrame;

    public bool Active { get; set; }

    public bool PauseWhenFaded { get; set; }

    public bool Dead { get; set; }

    public bool RemoveRequested { get; set; }
}
