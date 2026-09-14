namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Threading;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class QueueViewModel : ObservableObject, IDisposable
{
    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop-queue");
    private readonly IDisposable _subscription;
    private readonly SynchronizationContext? _sync;
    private long _seq;

    [ObservableProperty]
    private QueueItemVm? selected;

    public ObservableCollection<QueueItemVm> Items { get; } = [];

    public QueueViewModel(ICommandBus bus, SynchronizationContext? sync = null)
    {
        _bus = bus;
        _sync = sync;
        _subscription = bus.Subscribe(Apply);
        var snapshot = bus.Snapshot();
        Rebuild(snapshot.Queue.Items, snapshot.Transport.Current);
    }

    [RelayCommand]
    private void ClearQueue() => Submit(new ClearQueue());

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Selected is not { } item)
        {
            return;
        }
        RemoveItem(item);
    }

    public void PlayItem(QueueItemVm item)
    {
        if (item.EntryId is { } entry)
        {
            Submit(new JumpTo(entry));
            return;
        }
        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }
        if (index > 0)
        {
            Submit(new MoveQueueItem(index, 0));
        }
        Submit(new Next());
    }

    public void RemoveItem(QueueItemVm item)
    {
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Submit(new RemoveFromQueue(index));
        }
    }

    public void MoveItem(int from, int to)
    {
        if (from < 0 || from >= Items.Count || to < 0 || to >= Items.Count)
        {
            return;
        }
        Submit(new MoveQueueItem(from, to));
    }

    public void FocusPlaying()
    {
        var current = Items.FirstOrDefault(item => item.IsCurrent);
        if (current is not null)
        {
            Selected = current;
        }
    }

    public void Dispose() => _subscription.Dispose();

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

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private void Apply(StateEvent e)
    {
        Post(() => ApplyOnUi(e));
    }

    private void ApplyOnUi(StateEvent e)
    {
        switch (e)
        {
            case QueueDelta delta:
                Rebuild(delta.State.Items, null);
                break;
            case TransportDelta delta:
                MarkCurrent(delta.State.Current);
                break;
        }
    }

    private void Rebuild(ImmutableArray<QueueItem> items, DeckContent? current)
    {
        var selectedId = Selected?.Id;
        Items.Clear();
        foreach (var item in items)
        {
            var isCurrent = current is { } deck && item.EntryId is { } entryId && deck.EntryId == entryId;
            Items.Add(new QueueItemVm(
                item.EntryId?.Value ?? item.TrackId.Value,
                item.DisplayName,
                isCurrent,
                item.EntryId));
        }
        Selected = Items.FirstOrDefault(i => i.Id == selectedId);
    }

    private void MarkCurrent(DeckContent? current)
    {
        foreach (var item in Items)
        {
            item.IsCurrent = current is { } deck && deck.EntryId is { } entryId && item.RowKey == entryId.Value;
        }
    }

    public sealed class QueueItemVm
    {
        public QueueItemVm(Guid id, string displayName, bool isCurrent, EntryId? entryId = null)
        {
            Id = id;
            DisplayName = displayName;
            IsCurrent = isCurrent;
            EntryId = entryId;
        }

        public Guid Id { get; }

        public EntryId? EntryId { get; }

        public string DisplayName { get; }

        public bool IsCurrent { get; set; }

        public Guid RowKey => Id;
    }
}
