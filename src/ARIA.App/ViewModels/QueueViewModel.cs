namespace Aria.App.ViewModels;

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Aria.Core.Commands;
using Aria.Core.Model;
using Aria.Core.Runtime;
using Aria.Core.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class QueueViewModel : ObservableObject, IDisposable
{
    private readonly ICommandBus _bus;
    private readonly ClientId _client = new("desktop");
    private readonly IDisposable _subscription;
    private long _seq;

    [ObservableProperty]
    private bool locked;

    [ObservableProperty]
    private QueueItemVm? selected;

    public ObservableCollection<QueueItemVm> Items { get; } = [];

    public QueueViewModel(ICommandBus bus)
    {
        _bus = bus;
        _subscription = bus.Subscribe(Apply);
        var snapshot = bus.Snapshot();
        Rebuild(snapshot.Queue.Items, snapshot.Transport.Current);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ClearQueue() => Submit(new ClearQueue());

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveSelected()
    {
        if (Selected is not { } item)
        {
            return;
        }
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Submit(new RemoveFromQueue(index));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void MoveUp()
    {
        if (Selected is not { } item)
        {
            return;
        }
        var index = Items.IndexOf(item);
        Submit(new MoveQueueItem(index, Math.Max(0, index - 1)));
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void MoveDown()
    {
        if (Selected is not { } item)
        {
            return;
        }
        var index = Items.IndexOf(item);
        Submit(new MoveQueueItem(index, Math.Min(Items.Count - 1, index + 1)));
    }

    public bool CanEdit() => !Locked;

    public void Dispose() => _subscription.Dispose();

    private void Submit(Command command) => _bus.Submit(_client, Interlocked.Increment(ref _seq), command);

    private void Apply(StateEvent e)
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
                item.Color,
                isCurrent));
        }
        Selected = Items.FirstOrDefault(i => i.Id == selectedId);
    }

    private void MarkCurrent(DeckContent? current)
    {
        foreach (var item in Items)
        {
            item.IsCurrent = current is { } deck && deck.EntryId is { } entryId && item.EntryIdValue == entryId.Value;
        }
    }

    public sealed class QueueItemVm
    {
        public QueueItemVm(Guid id, string displayName, string? color, bool isCurrent)
        {
            Id = id;
            DisplayName = displayName;
            Color = color;
            IsCurrent = isCurrent;
        }

        public Guid Id { get; }

        public string DisplayName { get; }

        public string? Color { get; }

        public bool IsCurrent { get; set; }

        public Guid EntryIdValue => Id;
    }
}
