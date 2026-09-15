namespace Aria.Audio;

using Aria.Core.Model;
using Aria.Core.Playback;

internal enum CommandKind : byte
{
    Add,
    Remove,
    Transport,
    SetMix,
    SetAudio,
    Seek,
    StopAll,
}

internal readonly record struct MixerCommand(CommandKind Kind, MixerVoice? Voice, StreamHandle Handle, TransportCommand Transport, MixParameters? Mix, TimeSpan Duration, long SeekFrame = 0, TrackAudioSettings? Audio = null);
