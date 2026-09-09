namespace Aria.Core.Playback;

using Aria.Core.State;

public sealed record PositionSnapshot(DeckContent Deck, TimeSpan FilePosition, TimeSpan Remaining);

public sealed class PlaybackMonitor
{
    private readonly object _gate = new();
    private readonly Dictionary<StreamHandle, DeckContent> _bindings = [];
    private StreamHandle? _latestHandle;
    private volatile PositionSnapshot? _latest;

    public PositionSnapshot? Latest => _latest;

    public event Action<PositionSnapshot>? Changed;

    public void Bind(StreamHandle handle, DeckContent deck)
    {
        lock (_gate)
        {
            _bindings[handle] = deck;
        }
    }

    public void Unbind(StreamHandle handle)
    {
        lock (_gate)
        {
            if (!_bindings.Remove(handle))
            {
                return;
            }
            if (_latestHandle == handle)
            {
                _latestHandle = null;
                _latest = null;
            }
        }
    }

    public void Publish(StreamHandle handle, TimeSpan streamPosition)
    {
        PositionSnapshot snapshot;
        lock (_gate)
        {
            if (!_bindings.TryGetValue(handle, out var deck))
            {
                return;
            }
            _latestHandle = handle;
            var filePosition = deck.CueIn + streamPosition;
            var remaining = (deck.CueOut ?? deck.Duration) - filePosition;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }
            snapshot = new PositionSnapshot(deck, filePosition, remaining);
            _latest = snapshot;
        }
        Changed?.Invoke(snapshot);
    }
}
