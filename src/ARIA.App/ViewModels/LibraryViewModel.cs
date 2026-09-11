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
    private readonly IWaveformStore _waveforms;
    private readonly TrackImporter _importer;
    private readonly Func<TopLevel?>? _topLevel;
    private readonly ClientId _client = new("desktop");
    private readonly IDisposable _subscription;
    private long _seq;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = "готов";

    [ObservableProperty]
    private TrackVm? selected;

    public ObservableCollection<TrackVm> Tracks { get; } = [];

    public LibraryViewModel(
        ICommandBus bus,
        ILibraryStore library,
        IWaveformStore waveforms,
        TrackImporter importer,
        Func<TopLevel?>? topLevel = null)
    {
        _bus = bus;
        _library = library;
        _waveforms = waveforms;
        _importer = importer;
        _topLevel = topLevel;
        _subscription = bus.Subscribe(Apply);
        Reload();
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
            var importedCount = 0;
            foreach (var filePath in filePaths)
            {
                StatusText = $"импорт: {Path.GetFileName(filePath)}";
                var imported = await Task.Run(() => _importer.Import(filePath));
                if (imported is null)
                {
                    StatusText = $"не удалось открыть: {Path.GetFileName(filePath)}";
                    continue;
                }
                if (Upsert(imported.Track) && imported.Peaks is { } peaks)
                {
                    _waveforms.Save(peaks);
                }
                importedCount++;
            }
            Reload();
            if (Selected is null && Tracks.Count > 0)
            {
                Selected = Tracks[0];
            }
            StatusText = importedCount == filePaths.Length
                ? $"импортировано: {importedCount}"
                : $"импортировано: {importedCount} из {filePaths.Length}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSendToActive))]
    private void AddToActive()
    {
        if (Selected is not { } track || _bus.Snapshot().Show.ActiveId is not { } active)
        {
            return;
        }
        Submit(new AddEntry(active, track.Id));
    }

    private bool CanSendToActive() => Selected is not null && _bus.Snapshot().Show.ActiveId is not null;

    [RelayCommand(CanExecute = nameof(CanEnqueueSelected))]
    private void EnqueueSelected()
    {
        if (Selected is { } track)
        {
            EnqueueTrack(track);
        }
    }

    private bool CanEnqueueSelected() => Selected is not null;

    public void EnqueueTrack(TrackVm track) => Submit(new EnqueueTrack(track.Id));

    public void Dispose() => _subscription.Dispose();

    partial void OnSelectedChanged(TrackVm? value)
    {
        AddToActiveCommand.NotifyCanExecuteChanged();
        EnqueueSelectedCommand.NotifyCanExecuteChanged();
    }

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private bool Upsert(Track track)
    {
        var (tracks, playlists) = _library.Load();
        if (tracks.Any(t => t.FilePath == track.FilePath))
        {
            return false;
        }
        var merged = tracks.Add(track);
        _library.Upsert(merged, playlists);
        SyncShowState(merged);
        return true;
    }

    private void SyncShowState(ImmutableArray<Track> tracks)
    {
        var snapshot = _bus.Snapshot();
        Submit(new RestoreShow(
            tracks,
            snapshot.Show.Playlists,
            snapshot.Show.ActiveId,
            snapshot.Queue.Items,
            snapshot.Mixer.MasterGainDb,
            snapshot.Mixer.PanicFade,
            snapshot.Show.Clock.Elapsed,
            snapshot.Show.Clock.Running));
    }

    private void Reload()
    {
        var tracks = _library.Load().Tracks;
        var selectedId = Selected?.Id;
        Tracks.Clear();
        foreach (var track in tracks)
        {
            Tracks.Add(new TrackVm(track.Id, track.DefaultName, track.FilePath, track.Duration));
        }
        Selected = Tracks.FirstOrDefault(t => t.Id == selectedId);
    }

    private void Apply(StateEvent e)
    {
        if (e is ShowDelta)
        {
            AddToActiveCommand.NotifyCanExecuteChanged();
        }
    }

    public sealed record TrackVm(TrackId Id, string Name, string FilePath, TimeSpan Duration)
    {
        public string DurationText => Duration.ToString(@"mm\:ss");
    }
}
