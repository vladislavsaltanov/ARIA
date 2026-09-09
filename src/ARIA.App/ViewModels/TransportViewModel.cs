namespace Aria.App.ViewModels;

using System.ComponentModel;
using Aria.Core.Commands;
using Aria.Core.Playback;
using Aria.Core.Runtime;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class TransportViewModel : ObservableObject, IDisposable
{
    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop");
    private readonly SynchronizationContext? _sync;
    private readonly IDisposable _subscription;
    private readonly IDisposable? _monitorSubscription;
    private long _seq;

    [ObservableProperty]
    private string statusText = "STOP";

    [ObservableProperty]
    private string displayName = "—";

    [ObservableProperty]
    private string remaining = "--:--";

    [ObservableProperty]
    private string nextName = "—";

    [ObservableProperty]
    private bool panicked;

    [ObservableProperty]
    private bool locked;

    public TransportViewModel(ICommandBus bus, PlaybackMonitor? monitor = null, SynchronizationContext? sync = null)
    {
        _bus = bus;
        _sync = sync;
        _subscription = bus.Subscribe(e =>
        {
            if (e is TransportDelta delta)
            {
                Post(() => Apply(delta.State));
            }
        });
        if (monitor is not null)
        {
            _monitorSubscription = new MonitorSubscription(monitor, OnPosition, () => Post(() => Remaining = "--:--"));
        }
        Apply(bus.Snapshot().Transport);
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play() => Submit(new Play());

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => Submit(new Pause());

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Submit(new Stop());

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next() => Submit(new Next());

    [RelayCommand(CanExecute = nameof(CanReplay))]
    private void Replay() => Submit(new Replay());

    [RelayCommand]
    private void Panic() => Submit(new Panic());

    [RelayCommand]
    private void ToggleLock() => Locked = !Locked;

    private bool CanPlay() => !Locked;

    private bool CanPause() => !Locked;

    private bool CanStop() => !Locked;

    private bool CanNext() => !Locked;

    private bool CanReplay() => !Locked;

    partial void OnLockedChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ReplayCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _monitorSubscription?.Dispose();
    }

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

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

    private void Apply(TransportState state)
    {
        StatusText = state.Status switch
        {
            TransportStatus.Playing => "PLAY",
            TransportStatus.Paused => "PAUSE",
            TransportStatus.Panicked => "PANIC",
            _ => "STOP",
        };
        DisplayName = state.Current?.DisplayName ?? "—";
        var next = state.Next?.DisplayName;
        NextName = string.IsNullOrEmpty(next) ? "—" : next;
        Panicked = state.Status == TransportStatus.Panicked;
    }

    private void OnPosition(PositionSnapshot snapshot)
    {
        Remaining = Format(snapshot.Remaining);
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
}
