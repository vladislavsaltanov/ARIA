namespace Aria.Core.Commands;

using System.Collections.Immutable;
using Aria.Core.Model;
using Aria.Core.State;

public readonly record struct ClientId(string Value);

public abstract record Command;

public sealed record Play : Command;

public sealed record Pause : Command;

public sealed record Stop : Command;

public sealed record Next : Command;

public sealed record Replay : Command;

public sealed record SeekTo(TimeSpan FilePosition) : Command;

public sealed record Panic : Command;

public sealed record JumpTo(EntryId Entry) : Command;

public sealed record EnqueueEntry(EntryId Entry) : Command;

public sealed record EnqueueTrack(TrackId Track) : Command;

public sealed record RemoveFromQueue(int Index) : Command;

public sealed record ClearQueue : Command;

public sealed record CreatePlaylist(string Name) : Command;

public sealed record RenamePlaylist(PlaylistId Id, string Name) : Command;

public sealed record DeletePlaylist(PlaylistId Id) : Command;

public sealed record SetActivePlaylist(PlaylistId Id) : Command;

public sealed record AddEntry(PlaylistId Playlist, TrackId Track, int? Index = null) : Command;

public sealed record RemoveEntry(EntryId Entry) : Command;

public sealed record MoveEntry(EntryId Entry, int NewIndex) : Command;

public sealed record SetEntryOverrides(EntryId Entry, PlaylistOverrides? Overrides) : Command;

public sealed record MoveQueueItem(int From, int To) : Command;

public sealed record SetMasterGain(double GainDb) : Command;

public sealed record SetMuted(bool Muted) : Command;

public sealed record SetLocked(bool Locked) : Command;

public sealed record CreateScript(string Name) : Command;

public sealed record RenameScript(ScriptId Id, string Name) : Command;

public sealed record DeleteScript(ScriptId Id) : Command;

public sealed record AddScriptLine(ScriptId Script, TimeSpan AtElapsed, string Text, ImmutableArray<TrackId> Mentions) : Command;

public sealed record UpdateScriptLine(ScriptId Script, ScriptLineId Line, TimeSpan AtElapsed, string Text, ImmutableArray<TrackId> Mentions) : Command;

public sealed record RemoveScriptLine(ScriptId Script, ScriptLineId Line) : Command;

public sealed record MoveScriptLine(ScriptId Script, ScriptLineId Line, int NewIndex) : Command;

public sealed record TickShowClock : Command;

public sealed record StartShowClock : Command;

public sealed record PauseShowClock : Command;

public sealed record ResetShowClock : Command;

public sealed record SetPanicFade(TimeSpan Duration) : Command;

public sealed record LoadShow(
    ImmutableArray<Track> Tracks,
    ImmutableArray<Playlist> Playlists,
    PlaylistId? Active,
    ImmutableArray<Script> Scripts = default) : Command;

public sealed record RestoreShow(
    ImmutableArray<Track> Tracks,
    ImmutableArray<Playlist> Playlists,
    PlaylistId? Active,
    ImmutableArray<QueueItem> Queue,
    double MasterGainDb,
    TimeSpan PanicFade,
    TimeSpan ClockElapsed,
    bool ClockRunning,
    ImmutableArray<Script> Scripts = default) : Command;

public sealed record MergeTracks(ImmutableArray<Track> Tracks) : Command;
