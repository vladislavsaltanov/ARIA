namespace Aria.Core.State;

using System.Collections.Immutable;
using Aria.Core.Commands;
using Aria.Core.Model;

public enum StatePartition
{
    Show,
    Transport,
    Queue,
    Mixer,
}

public enum TransportStatus
{
    Stopped,
    Playing,
    Paused,
    Panicked,
}

public sealed record DeckContent(
    EntryId? EntryId,
    TrackId TrackId,
    string DisplayName,
    string? Color,
    EndAction EndAction,
    TimeSpan Duration,
    TimeSpan CueIn,
    TimeSpan? CueOut = null);

public sealed record TransportState(
    TransportStatus Status,
    DeckContent? Current,
    DeckContent? Next);

public sealed record QueueItem(
    EntryId? EntryId,
    TrackId TrackId,
    string DisplayName,
    string? Color);

public sealed record QueueState(ImmutableArray<QueueItem> Items);

public sealed record PlaylistsState(ImmutableArray<Playlist> Playlists, PlaylistId? ActiveId);

public sealed record MixerState(double MasterGainDb, TimeSpan PanicFade);

public abstract record StateEvent;

public sealed record ShowDelta(int Version, PlaylistsState State) : StateEvent;

public sealed record TransportDelta(int Version, TransportState State) : StateEvent;

public sealed record QueueDelta(int Version, QueueState State) : StateEvent;

public sealed record MixerDelta(int Version, MixerState State) : StateEvent;

public sealed record Rejected(ClientId Client, long Seq, string Reason) : StateEvent;

public sealed record ShowSnapshot(
    int ShowVersion,
    PlaylistsState Show,
    int TransportVersion,
    TransportState Transport,
    int QueueVersion,
    QueueState Queue,
    int MixerVersion,
    MixerState Mixer);
