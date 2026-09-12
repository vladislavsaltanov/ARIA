namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class PlaylistsViewModel : ObservableObject, IDisposable
{
    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop");
    private readonly Func<ImmutableArray<Track>>? _trackSource;
    private readonly WaveformThumbs? _thumbs;
    private readonly HashSet<TrackId> _faulted = [];
    private string? _awaitedPlaylistName;
    private readonly IDisposable _subscription;
    private ShowState? _lastShow;
    private TrackId? _linkedTrackId;
    private AppSettings _rowSettings = AppSettings.Default;
    private long _seq;

    [ObservableProperty]
    private PlaylistVm? selectedPlaylist;

    [ObservableProperty]
    private EntryVm? selectedEntry;

    [ObservableProperty]
    private string centerSearchText = string.Empty;

    [ObservableProperty]
    private string entryCountText = string.Empty;

    public ObservableCollection<PlaylistVm> Playlists { get; } = [];

    public ObservableCollection<EntryVm> VisibleEntries { get; } = [];

    public PlaylistsViewModel(ICommandBus bus, Func<ImmutableArray<Track>>? trackSource = null, WaveformThumbs? thumbs = null, AppSettings? rowSettings = null)
    {
        _bus = bus;
        _trackSource = trackSource;
        _thumbs = thumbs;
        _rowSettings = rowSettings ?? AppSettings.Default;
        _subscription = bus.Subscribe(Apply);
        Rebuild(bus.Snapshot().Show, _trackSource?.Invoke() ?? []);
    }

    public void UpdateRowSettings(AppSettings settings)
    {
        _rowSettings = settings;
        if (_lastShow is { } show)
        {
            Rebuild(show, _trackSource?.Invoke() ?? []);
        }
    }

    [RelayCommand]
    private void CreatePlaylist()
    {
        _awaitedPlaylistName = $"Новый плейлист {++_newPlaylistCounter}";
        Submit(new CreatePlaylist(_awaitedPlaylistName));
    }

    [RelayCommand]
    private void RenamePlaylist(string? name)
    {
        if (SelectedPlaylist is not { } playlist || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        Submit(new RenamePlaylist(playlist.Id, name));
    }

    [RelayCommand]
    private void DeletePlaylist()
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new DeletePlaylist(playlist.Id));
    }

    [RelayCommand]
    private void ActivatePlaylist()
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new SetActivePlaylist(playlist.Id));
    }

    [RelayCommand]
    private void RemoveEntry()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }
        RemoveEntryAt(entry);
    }

    public void RemoveEntryAt(EntryVm entry) => Submit(new RemoveEntry(entry.Id));

    public void DeletePlaylistAt(PlaylistVm playlist) => Submit(new DeletePlaylist(playlist.Id));

    public void MoveEntry(EntryId id, int newIndex)
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        var from = EntryIndex(id);
        if (from < 0 || newIndex < 0 || newIndex >= playlist.Entries.Count)
        {
            return;
        }
        Submit(new MoveEntry(id, newIndex));
    }

    public int EntryIndex(EntryId id)
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return -1;
        }
        for (var i = 0; i < playlist.Entries.Count; i++)
        {
            if (playlist.Entries[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    public void AddEntryAt(TrackId track, int index)
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new AddEntry(playlist.Id, track, Math.Clamp(index, 0, playlist.Entries.Count)));
    }

    public void EnqueueEntry(EntryVm entry) => Submit(new EnqueueEntry(entry.Id));

    public void SetLinkedTrack(TrackId? track)
    {
        if (_linkedTrackId == track)
        {
            return;
        }
        _linkedTrackId = track;
        if (_lastShow is { } show)
        {
            Rebuild(show, _trackSource?.Invoke() ?? []);
        }
    }

    public void Dispose() => _subscription.Dispose();

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private void Apply(StateEvent e)
    {
        switch (e)
        {
            case ShowDelta delta:
                Rebuild(delta.State, _trackSource?.Invoke() ?? []);
                break;
            case TransportDelta delta:
                var incoming = new HashSet<TrackId>(delta.State.Faulted);
                if (!incoming.SetEquals(_faulted))
                {
                    _faulted.Clear();
                    foreach (var id in incoming)
                    {
                        _faulted.Add(id);
                    }
                    if (_lastShow is { } show)
                    {
                        Rebuild(show, _trackSource?.Invoke() ?? []);
                    }
                }
                break;
        }
    }

    private int IndexOf(EntryId entryId)
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return -1;
        }
        for (var i = 0; i < playlist.Entries.Count; i++)
        {
            if (playlist.Entries[i].Id == entryId)
            {
                return i;
            }
        }
        return -1;
    }

    private void Rebuild(ShowState state, ImmutableArray<Track> tracks)
    {
        _lastShow = state;
        _newPlaylistCounter = Math.Max(_newPlaylistCounter, state.Playlists.Length);
        var trackNames = tracks.ToDictionary(t => t.Id, t => t.DefaultName);
        var trackDurations = tracks.ToDictionary(t => t.Id, t => t.Duration);
        var trackFiles = tracks.ToDictionary(t => t.Id, t => Path.GetFileName(t.FilePath));
        var selectedPlaylistId = SelectedPlaylist?.Id;
        var selectedEntryId = SelectedEntry?.Id;
        var awaitedName = _awaitedPlaylistName;

        Playlists.Clear();
        foreach (var playlist in state.Playlists)
        {
            var playlistVm = new PlaylistVm(playlist.Id, playlist.Name, playlist.Id == state.ActiveId);
            for (var index = 0; index < playlist.Entries.Length; index++)
            {
                var entry = playlist.Entries[index];
                var displayName = entry.Overrides?.Name ?? trackNames.GetValueOrDefault(entry.TrackId, $"track {entry.TrackId.Value:N}");
                var fileName = trackFiles.GetValueOrDefault(entry.TrackId, displayName);
                var duration = trackDurations.GetValueOrDefault(entry.TrackId, TimeSpan.Zero);
                var position = $"{index + 1:00}";
                playlistVm.Entries.Add(new EntryVm(
                    entry.Id,
                    entry.TrackId,
                    displayName,
                    fileName,
                    RowFormatter.Format(
                        _rowSettings.RowFormat,
                        position,
                        RowFormatter.DisplayName(_rowSettings, displayName, fileName),
                        fileName,
                        duration.ToString(@"mm\:ss")),
                    entry.Overrides?.Color,
                    entry.Overrides?.Note,
                    entry.Overrides,
                    _thumbs?.For(entry.TrackId),
                    _faulted.Contains(entry.TrackId),
                    position,
                    duration,
                    _linkedTrackId == entry.TrackId));
            }
            Playlists.Add(playlistVm);
        }

        PlaylistVm? awaited = null;
        if (awaitedName is not null)
        {
            awaited = Playlists.FirstOrDefault(p => p.Name == awaitedName);
            if (awaited is not null)
            {
                _awaitedPlaylistName = null;
            }
        }
        SelectedPlaylist = awaited ?? Playlists.FirstOrDefault(p => p.Id == selectedPlaylistId) ?? Playlists.FirstOrDefault();
        SelectedEntry = SelectedPlaylist?.Entries.FirstOrDefault(e => e.Id == selectedEntryId) ?? SelectedPlaylist?.Entries.FirstOrDefault();
        RefreshVisible();
        RefreshCenterHeader();
    }

    partial void OnSelectedPlaylistChanged(PlaylistVm? value)
    {
        RefreshVisible();
        RefreshCenterHeader();
    }

    partial void OnCenterSearchTextChanged(string value) => RefreshVisible();

    private void RefreshCenterHeader()
    {
        if (SelectedPlaylist is null)
        {
            EntryCountText = string.Empty;
            return;
        }
        var seconds = 0;
        foreach (var entry in SelectedPlaylist.Entries)
        {
            seconds += (int)entry.Duration.TotalSeconds;
        }
        var total = seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
        var count = SelectedPlaylist.Entries.Count;
        EntryCountText = $"{count} {TrackCountWord(count)} · {total}";
    }

    private static string TrackCountWord(int count)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return "трек";
        }
        if (mod10 is 2 or 3 or 4 && mod100 is not (12 or 13 or 14))
        {
            return "трека";
        }
        return "треков";
    }

    private void RefreshVisible()
    {
        VisibleEntries.Clear();
        if (SelectedPlaylist is null)
        {
            return;
        }
        foreach (var entry in SelectedPlaylist.Entries)
        {
            if (CenterSearchText.Length == 0 || entry.DisplayName.Contains(CenterSearchText, StringComparison.OrdinalIgnoreCase))
            {
                VisibleEntries.Add(entry);
            }
        }
    }

    private int _newPlaylistCounter;

    public sealed class PlaylistVm(PlaylistId id, string name, bool isActive)
    {
        public PlaylistId Id { get; } = id;

        public string Name { get; } = name;

        public bool IsActive { get; } = isActive;

        public ObservableCollection<EntryVm> Entries { get; } = [];
    }

    public sealed record EntryVm(
        EntryId Id,
        TrackId TrackId,
        string DisplayName,
        string FileName,
        string RowText,
        string? Color,
        string? Note,
        PlaylistOverrides? Overrides,
        StreamGeometry? Waveform,
        bool IsFaulted,
        string Position,
        TimeSpan Duration,
        bool IsLinked = false)
    {
        public bool HasOverrides => Overrides is not null;

        public bool HasNote => !string.IsNullOrEmpty(Note);

        public string DurationText => Duration.ToString(@"mm\:ss");

        public IBrush ColorBrush => string.IsNullOrEmpty(Color) ? Brushes.DimGray : new SolidColorBrush(Avalonia.Media.Color.Parse(Color));
    }
}
