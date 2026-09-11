namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Collections.ObjectModel;
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
    private long _seq;

    [ObservableProperty]
    private bool locked;

    [ObservableProperty]
    private PlaylistVm? selectedPlaylist;

    [ObservableProperty]
    private EntryVm? selectedEntry;

    public ObservableCollection<PlaylistVm> Playlists { get; } = [];

    public PlaylistsViewModel(ICommandBus bus, Func<ImmutableArray<Track>>? trackSource = null)
    {
        _bus = bus;
        _trackSource = trackSource;
        _bus.Subscribe(e =>
        {
            if (e is ShowDelta delta)
            {
                Rebuild(delta.State, _trackSource?.Invoke() ?? []);
            }
        });
        Rebuild(bus.Snapshot().Show, _trackSource?.Invoke() ?? []);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void CreatePlaylist() => Submit(new CreatePlaylist($"Новый плейлист {++_newPlaylistCounter}"));

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RenamePlaylist(string? name)
    {
        if (SelectedPlaylist is not { } playlist || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        Submit(new RenamePlaylist(playlist.Id, name));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void DeletePlaylist()
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new DeletePlaylist(playlist.Id));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ActivatePlaylist()
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }
        Submit(new SetActivePlaylist(playlist.Id));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveEntry()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }
        Submit(new RemoveEntry(entry.Id));
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveEntryUp()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }
        Submit(new MoveEntry(entry.Id, IndexOf(entry.Id) - 1));
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveEntryDown()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }
        Submit(new MoveEntry(entry.Id, IndexOf(entry.Id) + 1));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SetEntryName(string? name)
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }
        var trimmed = name?.Trim();
        var overrides = string.IsNullOrEmpty(trimmed)
            ? ClearName(entry.Overrides)
            : (entry.Overrides ?? new PlaylistOverrides()) with { Name = trimmed };
        Submit(new SetEntryOverrides(entry.Id, overrides));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ClearEntryOverrides() => Submit(new SetEntryOverrides(SelectedEntry!.Id, null));

    public bool CanEdit() => !Locked;

    public bool CanMoveUp() => !Locked && SelectedEntry is { } entry && IndexOf(entry.Id) > 0;

    public bool CanMoveDown() => !Locked && SelectedEntry is { } entry && SelectedPlaylist is { } playlist && IndexOf(entry.Id) < playlist.Entries.Count - 1;

    public void Dispose()
    {
    }

    private static PlaylistOverrides? ClearName(PlaylistOverrides? overrides)
    {
        if (overrides is null || overrides.Name is null)
        {
            return overrides;
        }
        return overrides with { Name = null };
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

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private void Rebuild(ShowState state, ImmutableArray<Track> tracks)
    {
        var trackNames = tracks.ToDictionary(t => t.Id, t => t.DefaultName);
        var selectedPlaylistId = SelectedPlaylist?.Id;
        var selectedEntryId = SelectedEntry?.Id;

        Playlists.Clear();
        foreach (var playlist in state.Playlists)
        {
            var playlistVm = new PlaylistVm(playlist.Id, playlist.Name, playlist.Id == state.ActiveId);
            foreach (var entry in playlist.Entries)
            {
                var displayName = entry.Overrides?.Name ?? trackNames.GetValueOrDefault(entry.TrackId, $"track {entry.TrackId.Value:N}");
                playlistVm.Entries.Add(new EntryVm(
                    entry.Id,
                    entry.TrackId,
                    displayName,
                    entry.Overrides?.Color,
                    entry.Overrides?.Note,
                    entry.Overrides));
            }
            Playlists.Add(playlistVm);
        }

        SelectedPlaylist = Playlists.FirstOrDefault(p => p.Id == selectedPlaylistId) ?? Playlists.FirstOrDefault();
        SelectedEntry = SelectedPlaylist?.Entries.FirstOrDefault(e => e.Id == selectedEntryId) ?? SelectedPlaylist?.Entries.FirstOrDefault();
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
        string? Color,
        string? Note,
        PlaylistOverrides? Overrides)
    {
        public bool HasOverrides => Overrides is not null;

        public bool HasNote => !string.IsNullOrEmpty(Note);

        public IBrush ColorBrush => string.IsNullOrEmpty(Color) ? Brushes.DimGray : new SolidColorBrush(Avalonia.Media.Color.Parse(Color));
    }
}
