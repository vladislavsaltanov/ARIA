namespace Aria.Core.Playback;

using Aria.Core.State;

public sealed record PositionSnapshot(DeckContent Deck, TimeSpan FilePosition, TimeSpan Remaining);

public sealed record LufsSnapshot(double MomentaryLufs);

public sealed class MeterMonitor
{
    private volatile LufsSnapshot? _latest;

    public LufsSnapshot? Latest => _latest;

    public event Action<LufsSnapshot>? Changed;

    public void Publish(double momentaryLufs)
    {
        var snapshot = new LufsSnapshot(momentaryLufs);
        _latest = snapshot;
        Changed?.Invoke(snapshot);
    }
}

public sealed class PlaybackMonitor
{
    private readonly object _gate = new();
    private readonly Dictionary<StreamHandle, DeckContent> _bindings = [];
    private StreamHandle? _latestHandle;
    private volatile PositionSnapshot? _latest;

    // Latest-value only: position stays out of versioned state.
    public PositionSnapshot? Latest => _latest;

    public event Action<PositionSnapshot>? Changed;

    public event Action? Cleared;

    public void Bind(StreamHandle handle, DeckContent deck)
    {
        lock (_gate)
        {
            _bindings[handle] = deck;
        }
    }

    public void Unbind(StreamHandle handle)
    {
        bool cleared;
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
                cleared = true;
            }
            else
            {
                cleared = false;
            }
        }
        if (cleared)
        {
            Cleared?.Invoke();
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
