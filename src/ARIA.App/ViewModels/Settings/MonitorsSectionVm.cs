namespace Aria.App.ViewModels.Settings;

using System.Collections.ObjectModel;
using System.Globalization;
using Aria.App.Services;
using Aria.Remote.Monitor;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class ListenerRowVm : ObservableObject
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly IMonitorControl _control;
    private ListenerLevels _levels;
    private string _name;
    private string _syncText;

    public ListenerRowVm(ListenerProfile profile, IMonitorControl control)
    {
        Id = profile.Id;
        _control = control;
        _name = profile.Name;
        _levels = profile.Levels;
        _syncText = FormatSync(profile.Stats);
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public string SyncText
    {
        get => _syncText;
        private set => SetProperty(ref _syncText, value);
    }

    public double BackingDb
    {
        get => _levels.BackingDb;
        set => ChangeLevels(_levels with { BackingDb = Math.Clamp(value, -80.0, 12.0) });
    }

    public double ClickDb
    {
        get => _levels.ClickDb;
        set => ChangeLevels(_levels with { ClickDb = Math.Clamp(value, -80.0, 12.0) });
    }

    public void Update(ListenerProfile profile)
    {
        Name = profile.Name;
        SyncText = FormatSync(profile.Stats);
        if (profile.Levels != _levels)
        {
            _levels = profile.Levels;
            OnPropertyChanged(nameof(BackingDb));
            OnPropertyChanged(nameof(ClickDb));
        }
    }

    public static string FormatSync(ListenerStats? stats) =>
        stats is { State: "locked" } locked
            ? string.Format(Russian, "±{0:0.0} мс", locked.UncertaintyUs / 1000.0)
            : "нет синхр.";

    private void ChangeLevels(ListenerLevels levels)
    {
        if (levels == _levels)
        {
            return;
        }
        _levels = levels;
        OnPropertyChanged(nameof(BackingDb));
        OnPropertyChanged(nameof(ClickDb));
        _control.SetLevels(Id, levels);
    }

    [RelayCommand]
    private void Kick() => _control.Kick(Id);
}

public sealed partial class MonitorsSectionVm : ObservableObject, IDisposable
{
    public const double MinHallTrimMs = -50;
    public const double MaxHallTrimMs = 200;

    private readonly IMonitorControl _control;
    private readonly SynchronizationContext? _sync;
    private readonly Timer _timer;
    private double _hallTrimMs;
    private bool _testClickEnabled;
    private string _deviceBufferText = string.Empty;

    public MonitorsSectionVm(IMonitorControl control, SynchronizationContext? sync = null)
    {
        _control = control;
        _sync = sync;
        _hallTrimMs = control.HallTrimMs;
        Refresh();
        _timer = new Timer(_ => Post(Refresh), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ObservableCollection<ListenerRowVm> Listeners { get; } = [];

    public double HallTrimMs
    {
        get => _hallTrimMs;
        set
        {
            if (SetProperty(ref _hallTrimMs, Math.Clamp(value, MinHallTrimMs, MaxHallTrimMs)))
            {
                _control.HallTrimMs = _hallTrimMs;
            }
        }
    }

    public bool TestClickEnabled
    {
        get => _testClickEnabled;
        set
        {
            if (SetProperty(ref _testClickEnabled, value))
            {
                _control.TestClickEnabled = value;
            }
        }
    }

    public string DeviceBufferText
    {
        get => _deviceBufferText;
        private set => SetProperty(ref _deviceBufferText, value);
    }

    public void Dispose() => _timer.Dispose();

    public void Refresh()
    {
        var profiles = _control.Listeners;
        for (var i = Listeners.Count - 1; i >= 0; i--)
        {
            if (profiles.All(p => p.Id != Listeners[i].Id))
            {
                Listeners.RemoveAt(i);
            }
        }
        foreach (var profile in profiles)
        {
            var row = Listeners.FirstOrDefault(r => r.Id == profile.Id);
            if (row is null)
            {
                Listeners.Add(new ListenerRowVm(profile, _control));
            }
            else
            {
                row.Update(profile);
            }
        }
        SetProperty(ref _testClickEnabled, _control.TestClickEnabled, nameof(TestClickEnabled));
        DeviceBufferText = string.Format(CultureInfo.InvariantCulture, "буфер устройства: {0:0} мс", _control.DeviceBufferMs);
    }

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
}
