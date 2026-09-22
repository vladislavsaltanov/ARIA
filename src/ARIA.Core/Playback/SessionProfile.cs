namespace Aria.Core.Playback;

public sealed record SessionProfile(PreviewSessionHandle Session, string Name, double BackingGainDb, bool ClickMuted);
