namespace Aria.App.ViewModels.Settings;

using System.Collections.ObjectModel;
using Aria.Core.Commands;
using Aria.Core.Playback;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class MonitorSessionVm : ObservableObject
{
    private readonly Action<Command> _submit;
    private string _name;
    private double _backingGainDb;
    private double _clickGainDb;

    public MonitorSessionVm(PreviewSessionHandle session, string name, double backingGainDb, double clickGainDb, Action<Command> submit)
    {
        Session = session;
        _name = name;
        _backingGainDb = backingGainDb;
        _clickGainDb = clickGainDb;
        _submit = submit;
    }

    public PreviewSessionHandle Session { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double BackingGainDb
    {
        get => _backingGainDb;
        set
        {
            if (SetProperty(ref _backingGainDb, Math.Clamp(value, -80.0, 12.0)))
            {
                _submit(new SetSessionBackingGain(Session, _backingGainDb));
            }
        }
    }

    public double ClickGainDb
    {
        get => _clickGainDb;
        set
        {
            if (SetProperty(ref _clickGainDb, Math.Clamp(value, -80.0, 12.0)))
            {
                _submit(new SetSessionClickGain(Session, _clickGainDb));
            }
        }
    }

    public void UpdateName(string name) => Name = name;

    [RelayCommand]
    private void Kick() => _submit(new CloseSession(Session));
}

public sealed partial class MonitorsSectionVm : ObservableObject, IDisposable
{
    private readonly Action<Command> _submit;
    private readonly Func<IReadOnlyList<SessionProfile>> _sessions;
    private readonly SynchronizationContext? _sync;
    private readonly Timer _timer;

    public MonitorsSectionVm(Action<Command> submit, Func<IReadOnlyList<SessionProfile>> sessions, SynchronizationContext? sync = null)
    {
        _submit = submit;
        _sessions = sessions;
        _sync = sync;
        Refresh();
        _timer = new Timer(_ => Post(Refresh), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public ObservableCollection<MonitorSessionVm> Sessions { get; } = [];

    public void Dispose() => _timer.Dispose();

    public void Refresh()
    {
        var profiles = _sessions();
        for (var i = Sessions.Count - 1; i >= 0; i--)
        {
            if (profiles.All(p => p.Session != Sessions[i].Session))
            {
                Sessions.RemoveAt(i);
            }
        }
        foreach (var profile in profiles)
        {
            var row = Sessions.FirstOrDefault(r => r.Session == profile.Session);
            if (row is null)
            {
                Sessions.Add(new MonitorSessionVm(profile.Session, profile.Name, profile.BackingGainDb, profile.ClickGainDb, _submit));
            }
            else
            {
                row.UpdateName(profile.Name);
            }
        }
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
