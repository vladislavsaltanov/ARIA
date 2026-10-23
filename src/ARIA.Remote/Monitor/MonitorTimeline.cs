namespace Aria.Remote.Monitor;

using System.Collections.Immutable;

internal sealed record TimelineMessage(string Type, long Seq, double Rate, ImmutableArray<VoiceTimeline> Voices, GridTimeline? Grid, DeckTimeline Deck, TestTimeline? Test);

internal sealed record VoiceTimeline(int Id, int Epoch, Guid Track, string Kind, bool Fallback, double GainDb, long AnchorUs, long AnchorFrame, bool Playing, long? EndFrame, FadeTimeline? Fade);

internal sealed record FadeTimeline(double From, double To, double Progress, int Frames, string Curve, bool StopAtEnd);

internal sealed record GridTimeline(int Voice, double Bpm, int BeatsPerBar, long FirstBeatFrame);

internal sealed record DeckTimeline(string Status, string? Title, NextTimeline? Next);

internal sealed record NextTimeline(string Title, Guid Track, string Kind, long StartFrame);

internal sealed record TestTimeline(long AnchorUs, double IntervalUs);
