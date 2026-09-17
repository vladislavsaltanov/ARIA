namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Globalization;
using Aria.App.Services;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class TransportViewModel : ObservableObject, IDisposable
{
    private const double VolumeMinDb = -80.0;
    private const double VolumeMaxDb = 12.0;
    private static readonly SolidColorBrush BrushFg = new(Color.Parse("#ECECEC"));
    private static readonly SolidColorBrush BrushDim = new(Color.Parse("#8A8A8A"));
    private static readonly SolidColorBrush BrushFaulted = new(Color.Parse("#E5484D"));
    private static readonly SolidColorBrush BrushOk = new(Color.Parse("#3FB950"));
    private static readonly SolidColorBrush BrushWarm = new(Color.Parse("#D29922"));

    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop-transport");
    private readonly Func<ImmutableArray<Track>>? _trackSource;
    private AppSettings _rowSettings = AppSettings.Default;
    private TransportState? _lastTransport;
    private readonly SynchronizationContext? _sync;
    private readonly IDisposable _subscription;
    private readonly IDisposable? _monitorSubscription;
    private readonly MeterMonitor? _meters;
    private long _seq;
    private double _masterGainDb;
    private double _volumePercent = DbToPercent(0.0);
    private bool _playing;
    private double _lastLufs = double.NaN;
    private float _lastPeakLeft;
    private float _lastPeakRight;
    private LufsMeterZones _zones = LufsMeterZones.Default;
    private string _trackElapsedText = "--:--";
    private string _timeOfDayText = "--:--:--";

    [ObservableProperty]
    private string statusText = "STOP";

    [ObservableProperty]
    private string displayName = "—";

    [ObservableProperty]
    private string remaining = "--:--";

    [ObservableProperty]
    private string nextLine = "—";

    [ObservableProperty]
    private bool panicked;

    [ObservableProperty]
    private bool locked;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool muted;

    [ObservableProperty]
    private string lufsText = "—";

    [ObservableProperty]
    private double lufsLevel;

    [ObservableProperty]
    private bool lufsHot;

    [ObservableProperty]
    private IBrush lufsBarBrush = BrushFg;

    [ObservableProperty]
    private double levelLeft;

    [ObservableProperty]
    private double levelRight;

    [ObservableProperty]
    private bool levelHot;

    [ObservableProperty]
    private IBrush levelBarBrush = BrushFg;

    [ObservableProperty]
    private IBrush lockBrush = BrushDim;

    [ObservableProperty]
    private string showClockText = "00:00:00";

    [ObservableProperty]
    private bool clockRunning;

    [ObservableProperty]
    private string timerSubText = "--:--:-- · --:--";

    [ObservableProperty]
    private double positionFraction;

    [ObservableProperty]
    private TrackId? currentTrackId;

    public TransportViewModel(
        ICommandBus bus,
        PlaybackMonitor? monitor = null,
        SynchronizationContext? sync = null,
        MeterMonitor? meters = null,
        Func<ImmutableArray<Track>>? trackSource = null,
        AppSettings? rowSettings = null)
    {
        _bus = bus;
        _trackSource = trackSource;
        _rowSettings = rowSettings ?? AppSettings.Default;
        _sync = sync;
        _subscription = bus.Subscribe(ApplyEvent);
        if (monitor is not null)
        {
            _monitorSubscription = new MonitorSubscription(monitor, OnPosition, () => Post(() =>
            {
                Remaining = "--:--";
                _trackElapsedText = "--:--";
                PositionFraction = 0;
                RefreshTimerSubText();
            }));
        }
        if (meters is not null)
        {
            _meters = meters;
            meters.Changed += OnLufs;
        }
        var snapshot = bus.Snapshot();
        _masterGainDb = snapshot.Mixer.MasterGainDb;
        _volumePercent = DbToPercent(_masterGainDb);
        Apply(snapshot.Transport);
        ApplyShow(snapshot.Show);
        ApplyMixer(snapshot.Mixer);
        RefreshWallClock();
    }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (Math.Abs(value - 100.0) <= 2.0)
            {
                value = 100.0;
            }
            if (SetProperty(ref _volumePercent, value))
            {
                OnPropertyChanged(nameof(VolumePercentText));
                Submit(new SetMasterGain(PercentToDb(value)));
            }
        }
    }

    public string VolumeDbText =>
        string.Create(CultureInfo.InvariantCulture, $"Громкость — {_masterGainDb:F1} дБ");

    public string VolumePercentText => $"{_volumePercent:F0}%";

    [RelayCommand]
    private void Play() => Submit(new Play());

    [RelayCommand]
    private void Pause() => Submit(new Pause());

    [RelayCommand]
    private void Stop() => Submit(new Stop());

    [RelayCommand]
    private void Next() => Submit(new Next());

    [RelayCommand]
    private void Replay() => Submit(new Replay());

    [RelayCommand]
    private void Panic() => Submit(new Panic());

    [RelayCommand]
    private void TogglePlayPause() => Submit(_playing ? new Pause() : new Play());

    [RelayCommand]
    private void ToggleMute() => Submit(new SetMuted(!Muted));

    [RelayCommand]
    private void StartClock() => Submit(new StartShowClock());

    [RelayCommand]
    private void PauseClock() => Submit(new PauseShowClock());

    [RelayCommand]
    private void ResetClock() => Submit(new ResetShowClock());

    [RelayCommand]
    public void ToggleLock() => Submit(new SetLocked(!Locked));

    public void RefreshWallClock()
    {
        _timeOfDayText = DateTime.Now.ToString("HH:mm:ss");
        RefreshTimerSubText();
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _monitorSubscription?.Dispose();
        if (_meters is not null)
        {
            _meters.Changed -= OnLufs;
        }
    }

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    public void SeekToFilePosition(TimeSpan filePosition) => Submit(new SeekTo(filePosition));

    private void Post(Action work)
    {
        if (_sync is { } sync)
        {
            sync.Post(_ => work(), null);
        }
        else
        {
            work();
        }
    }

    private void ApplyEvent(StateEvent e)
    {
        switch (e)
        {
            case TransportDelta delta:
                Post(() => Apply(delta.State));
                break;
            case ShowDelta show:
                Post(() => ApplyShow(show.State));
                break;
            case MixerDelta mixer:
                Post(() => ApplyMixer(mixer.State));
                break;
        }
    }

    public void UpdateRowSettings(AppSettings settings)
    {
        _rowSettings = settings;
        if (_lastTransport is { } state)
        {
            Apply(state);
        }
    }

    private void Apply(TransportState state)
    {
        _lastTransport = state;
        StatusText = state.Status switch
        {
            TransportStatus.Playing => "PLAY",
            TransportStatus.Paused => "PAUSE",
            TransportStatus.Panicked => "PANIC",
            _ => "STOP",
        };
        DisplayName = state.Current is null ? "—" : Truncate(TrackDisplay(state.Current));
        CurrentTrackId = state.Current?.TrackId;
        NextLine = state.Next is null ? "—" : Truncate($"Далее: {TrackDisplay(state.Next)}");
        Panicked = state.Status == TransportStatus.Panicked;
        _playing = state.Status == TransportStatus.Playing;
        IsPlaying = _playing;
        RefreshLufs();
    }

    private void ApplyShow(ShowState state)
    {
        Locked = state.Locked;
        LockBrush = state.Locked ? BrushFg : BrushDim;
        ShowClockText = FormatClock(state.Clock.Elapsed);
        ClockRunning = state.Clock.Running;
    }

    private void ApplyMixer(MixerState state)
    {
        _zones = state.EffectiveGlobal.EffectiveZones;
        RefreshLufs();
        _masterGainDb = state.MasterGainDb;
        _volumePercent = DbToPercent(state.MasterGainDb);
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeDbText));
        OnPropertyChanged(nameof(VolumePercentText));
        Muted = state.Muted;
    }

    private void OnPosition(PositionSnapshot snapshot) => Post(() =>
    {
        Remaining = Format(snapshot.Remaining);
        _trackElapsedText = Format(snapshot.FilePosition);
        var total = (snapshot.Deck.CueOut ?? snapshot.Deck.Duration).TotalSeconds;
        PositionFraction = total > 0 ? Math.Clamp(snapshot.FilePosition.TotalSeconds / total, 0.0, 1.0) : 0.0;
        RefreshTimerSubText();
    });

    private void OnLufs(LufsSnapshot snapshot) => Post(() =>
    {
        _lastLufs = snapshot.MomentaryLufs;
        _lastPeakLeft = snapshot.PeakLeft;
        _lastPeakRight = snapshot.PeakRight;
        RefreshLufs();
    });

    private void RefreshLufs()
    {
        if (Panicked)
        {
            LufsText = "MUTED";
            LufsLevel = 0;
            LufsHot = false;
            LufsBarBrush = BrushFg;
            LevelLeft = 0;
            LevelRight = 0;
            LevelHot = false;
            LevelBarBrush = BrushFg;
            return;
        }
        if (double.IsNaN(_lastLufs))
        {
            LufsText = "—";
            LufsLevel = 0;
            LufsHot = false;
            LufsBarBrush = BrushFg;
            LevelLeft = 0;
            LevelRight = 0;
            LevelHot = false;
            LevelBarBrush = BrushFg;
            return;
        }
        LufsText = _lastLufs.ToString("F1", CultureInfo.InvariantCulture);
        LufsLevel = Math.Clamp((_lastLufs + 60.0) / 60.0, 0.0, 1.0);
        LufsBarBrush = ZoneBrush(_lastLufs, _zones);
        LufsHot = ReferenceEquals(LufsBarBrush, BrushFaulted);
        LevelLeft = Math.Clamp(_lastPeakLeft, 0.0, 1.0);
        LevelRight = Math.Clamp(_lastPeakRight, 0.0, 1.0);
        LevelHot = _lastPeakLeft >= 1.0f || _lastPeakRight >= 1.0f;
        LevelBarBrush = LevelHot ? BrushFaulted : BrushFg;
    }

    internal static SolidColorBrush ZoneBrush(double lufs, LufsMeterZones zones)
    {
        if (lufs < zones.GreenDb)
        {
            return BrushFg;
        }
        if (lufs < zones.YellowDb)
        {
            return BrushOk;
        }
        if (lufs < zones.RedDb)
        {
            return BrushWarm;
        }
        return BrushFaulted;
    }

    private void RefreshTimerSubText() => TimerSubText = $"{_timeOfDayText} · {_trackElapsedText}";

    private static double PercentToDb(double percent)
    {
        var fraction = Math.Clamp(percent, 0.0, 125.0) / 100.0;
        if (fraction <= 0.0)
        {
            return VolumeMinDb;
        }
        return Math.Clamp(40.0 * Math.Log10(fraction), VolumeMinDb, VolumeMaxDb);
    }

    private static double DbToPercent(double gainDb)
    {
        if (gainDb <= VolumeMinDb)
        {
            return 0.0;
        }
        return Math.Clamp(Math.Pow(10.0, gainDb / 40.0) * 100.0, 0.0, 125.0);
    }

    private sealed class MonitorSubscription : IDisposable
    {
        private readonly PlaybackMonitor _monitor;

        private readonly Action<PositionSnapshot> _handler;

        private readonly Action _cleared;

        public MonitorSubscription(PlaybackMonitor monitor, Action<PositionSnapshot> handler, Action cleared)
        {
            _monitor = monitor;
            _handler = handler;
            _cleared = cleared;
            monitor.Changed += handler;
            monitor.Cleared += _cleared;
        }

        public void Dispose()
        {
            _monitor.Changed -= _handler;
            _monitor.Cleared -= _cleared;
        }
    }

    private const int MaxNameLength = 100;

    private static string Truncate(string value) =>
        value.Length <= MaxNameLength ? value : value[..MaxNameLength] + "…";

    private string TrackDisplay(DeckContent deck)
    {
        var name = deck.DisplayName;
        if (_rowSettings.UseFileName && _trackSource?.Invoke() is { } tracks)
        {
            foreach (var track in tracks)
            {
                if (track.Id == deck.TrackId)
                {
                    name = Path.GetFileName(track.FilePath);
                    break;
                }
            }
        }
        return string.IsNullOrEmpty(name) ? "—" : name;
    }

    private static string Format(TimeSpan value)
    {
        var total = (int)Math.Ceiling(value.TotalSeconds);
        if (total < 0)
        {
            total = 0;
        }
        return total >= 3600
            ? $"{total / 3600}:{total % 3600 / 60:00}:{total % 60:00}"
            : $"{total / 60:00}:{total % 60:00}";
    }

    private static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}
