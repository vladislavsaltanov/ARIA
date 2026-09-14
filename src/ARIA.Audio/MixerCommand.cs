namespace Aria.Audio;

using Aria.Core.Playback;

internal enum CommandKind : byte
{
    Add,
    Remove,
    Transport,
    SetMix,
    Seek,
    StopAll,
}

internal readonly record struct MixerCommand(CommandKind Kind, MixerVoice? Voice, StreamHandle Handle, TransportCommand Transport, MixParameters? Mix, TimeSpan Duration, long SeekFrame = 0);
