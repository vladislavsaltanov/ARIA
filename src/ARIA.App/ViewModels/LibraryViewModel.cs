namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using Aria.Persistence;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class LibraryViewModel : ObservableObject, IDisposable
{
    private static readonly FilePickerFileType AudioFilter = new("Аудио")
    {
        Patterns = ["*.wav", "*.flac", "*.mp3", "*.ogg"],
    };

    private readonly ICommandBus _bus;
    private readonly ILibraryStore _library;
    private readonly WaveformThumbs? _thumbs;
    private readonly Func<IReadOnlyList<string>, IProgress<string>?, Task<ImportReport>> _import;
    private readonly Func<TopLevel?>? _topLevel;
    private readonly ClientId _client = new("desktop");
    private readonly IDisposable _subscription;
    private readonly HashSet<TrackId> _faulted = [];
    private long _seq;
    private string _searchText = string.Empty;
    private TrackId? _linkedTrackId;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "готов";

    [ObservableProperty]
    private TrackVm? selected;

    public ObservableCollection<TrackVm> Tracks { get; } = [];

    public ObservableCollection<TrackVm> FilteredTracks { get; } = [];

    public LibraryViewModel(
        ICommandBus bus,
        ILibraryStore library,
        Func<IReadOnlyList<string>, IProgress<string>?, Task<ImportReport>> import,
        Func<TopLevel?>? topLevel = null,
        WaveformThumbs? thumbs = null)
    {
        _bus = bus;
        _library = library;
        _import = import;
        _topLevel = topLevel;
        _thumbs = thumbs;
        _subscription = bus.Subscribe(Apply);
        Reload();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshFiltered();
            }
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        var topLevel = _topLevel?.Invoke();
        if (topLevel is null)
        {
            StatusText = "выбор файлов недоступен";
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт треков",
            AllowMultiple = true,
            FileTypeFilter = [AudioFilter],
        });
        if (files.Count == 0)
        {
            return;
        }
        await ImportAsync(files.Select(file => file.Path.LocalPath));
    }

    public async Task ImportAsync(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            return;
        }
        var filePaths = paths.ToArray();
        if (filePaths.Length == 0)
        {
            return;
        }
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(name => StatusText = $"импорт: {name}");
            var report = await _import(filePaths, progress);
            Reload();
            if (Selected is null && Tracks.Count > 0)
            {
                Selected = Tracks[0];
            }
            StatusText = FormatReport(report);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void EnqueueTrack(TrackVm track) => Submit(new EnqueueTrack(track.Id));

    public void SetLinkedTrack(TrackId? track)
    {
        if (_linkedTrackId == track)
        {
            return;
        }
        _linkedTrackId = track;
        Reload();
    }

    public void AddToPlaylist(PlaylistId playlist, TrackId track) => Submit(new AddEntry(playlist, track, null));

    public void EnqueueTracks(IEnumerable<TrackVm> tracks)
    {
        foreach (var track in tracks)
        {
            Submit(new EnqueueTrack(track.Id));
        }
    }

    public void Dispose() => _subscription.Dispose();

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private static string FormatReport(ImportReport report)
    {
        var text = $"добавлено {report.Added}, пропущено {report.Skipped}";
        if (report.Failed.Length > 0)
        {
            text += $", не удалось: {report.Failed.Length}";
        }
        return text;
    }

    private void Reload()
    {
        var tracks = _library.Load().Tracks;
        var selectedId = Selected?.Id;
        Tracks.Clear();
        foreach (var track in tracks)
        {
            Tracks.Add(new TrackVm(
                track.Id,
                track.DefaultName,
                track.FilePath,
                track.Duration,
                _thumbs?.For(track.Id),
                _faulted.Contains(track.Id),
                _linkedTrackId == track.Id));
        }
        Selected = Tracks.FirstOrDefault(t => t.Id == selectedId);
        RefreshFiltered();
    }

    private void RefreshFiltered()
    {
        FilteredTracks.Clear();
        foreach (var track in Tracks)
        {
            if (_searchText.Length == 0 || track.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredTracks.Add(track);
            }
        }
        if (Selected is null || !FilteredTracks.Contains(Selected))
        {
            Selected = FilteredTracks.FirstOrDefault();
        }
    }

    private void Apply(StateEvent e)
    {
        if (e is not TransportDelta delta)
        {
            return;
        }
        var incoming = new HashSet<TrackId>(delta.State.Faulted);
        if (!incoming.SetEquals(_faulted))
        {
            _faulted.Clear();
            foreach (var id in incoming)
            {
                _faulted.Add(id);
            }
            Reload();
        }
    }

    public sealed record TrackVm(
        TrackId Id,
        string Name,
        string FilePath,
        TimeSpan Duration,
        StreamGeometry? Waveform,
        bool IsFaulted,
        bool IsLinked = false)
    {
        public string DurationText => Duration.ToString(@"mm\:ss");
    }
}
