namespace Aria.Remote.Monitor;

using System.Collections.Concurrent;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;

internal sealed record PcmSlice(short[] Samples, int Frames, long? TotalFrames);

internal sealed class PcmCache : IDisposable
{
    public const int ChunkFrames = 48000;

    private const int Channels = 2;

    private readonly ISourceFactory _factory;
    private readonly IMonitorSources _sources;
    private readonly long _budgetBytes;
    private readonly object _gate = new();
    private readonly Dictionary<(TrackId Track, long Index), LinkedListNode<Entry>> _cached = [];
    private readonly LinkedList<Entry> _recency = [];
    private readonly Dictionary<(TrackId Track, long Index), TaskCompletionSource<Chunk>> _pending = [];
    private readonly HashSet<TrackId> _undecodable = [];
    private readonly Dictionary<TrackId, long> _totals = [];
    private readonly Dictionary<TrackId, Decoder> _decoders = [];
    private readonly BlockingCollection<(TrackId Track, long Index)> _queue = [];
    private readonly Thread _worker;
    private long _bytes;
    private int _decodedChunks;

    public PcmCache(ISourceFactory factory, IMonitorSources sources, long budgetBytes = 512L * 1024 * 1024)
    {
        _factory = factory;
        _sources = sources;
        _budgetBytes = budgetBytes;
        _worker = new Thread(Work) { IsBackground = true, Name = "aria-pcm", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    internal int DecodedChunks => Volatile.Read(ref _decodedChunks);

    public bool IsUndecodable(TrackId track)
    {
        lock (_gate)
        {
            return _undecodable.Contains(track);
        }
    }

    public async Task<PcmSlice> ReadAsync(TrackId track, long fromFrame, int frames, CancellationToken cancellationToken)
    {
        var samples = new short[frames * Channels];
        var filled = 0;
        var position = fromFrame;
        while (filled < frames)
        {
            var index = position / ChunkFrames;
            var chunk = await GetChunkAsync(track, index, cancellationToken);
            var offset = (int)(position - index * ChunkFrames);
            var available = chunk.Frames - offset;
            if (available <= 0)
            {
                break;
            }
            var take = Math.Min(available, frames - filled);
            chunk.Samples.AsSpan(offset * Channels, take * Channels).CopyTo(samples.AsSpan(filled * Channels));
            filled += take;
            position += take;
            if (chunk.Frames < ChunkFrames)
            {
                break;
            }
        }
        long? total;
        lock (_gate)
        {
            total = _totals.TryGetValue(track, out var known) ? known : null;
        }
        return new PcmSlice(filled == frames ? samples : samples[..(filled * Channels)], filled, total);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        lock (_gate)
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetCanceled();
            }
        }
        foreach (var decoder in _decoders.Values)
        {
            (decoder.Source as IDisposable)?.Dispose();
        }
        _queue.Dispose();
    }

    private Task<Chunk> GetChunkAsync(TrackId track, long index, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_undecodable.Contains(track))
            {
                throw new InvalidDataException("undecodable");
            }
            var key = (track, index);
            if (_cached.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return Task.FromResult(node.Value.Chunk);
            }
            if (_totals.TryGetValue(track, out var total) && index * ChunkFrames >= total)
            {
                return Task.FromResult(new Chunk([], 0));
            }
            if (!_pending.TryGetValue(key, out var pending))
            {
                pending = new TaskCompletionSource<Chunk>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[key] = pending;
                _queue.Add(key, CancellationToken.None);
            }
            return pending.Task.WaitAsync(cancellationToken);
        }
    }

    private void Work()
    {
        foreach (var key in _queue.GetConsumingEnumerable())
        {
            TaskCompletionSource<Chunk>? pending;
            lock (_gate)
            {
                _pending.TryGetValue(key, out pending);
            }
            try
            {
                var chunk = Decode(key.Track, key.Index);
                Store(key, chunk);
                pending?.TrySetResult(chunk);
            }
            catch (Exception e)
            {
                pending?.TrySetException(e);
            }
            finally
            {
                lock (_gate)
                {
                    _pending.Remove(key);
                }
            }
        }
    }

    private Chunk Decode(TrackId track, long index)
    {
        if (!_decoders.TryGetValue(track, out var decoder))
        {
            var source = _sources.Find(track) is { } found ? _factory.Open(found.FilePath, TimeSpan.Zero, null) : null;
            if (source is null || source.Channels != Channels)
            {
                (source as IDisposable)?.Dispose();
                lock (_gate)
                {
                    _undecodable.Add(track);
                }
                throw new InvalidDataException("undecodable");
            }
            decoder = new Decoder(source);
            _decoders[track] = decoder;
        }
        var start = index * ChunkFrames;
        if (decoder.Position != start)
        {
            decoder.Source.Seek(start);
            decoder.Position = start;
        }
        var total = 0;
        while (total < ChunkFrames)
        {
            var read = decoder.Source.ReadFrames(decoder.Scratch.AsSpan(total * Channels, (ChunkFrames - total) * Channels));
            if (read <= 0)
            {
                break;
            }
            total += read;
        }
        decoder.Position = start + total;
        var samples = new short[total * Channels];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            samples[sample] = (short)Math.Clamp(Math.Round(decoder.Scratch[sample] * 32767.0), short.MinValue, short.MaxValue);
        }
        Interlocked.Increment(ref _decodedChunks);
        if (total < ChunkFrames)
        {
            lock (_gate)
            {
                _totals[track] = start + total;
            }
        }
        return new Chunk(samples, total);
    }

    private void Store((TrackId Track, long Index) key, Chunk chunk)
    {
        lock (_gate)
        {
            if (_cached.ContainsKey(key))
            {
                return;
            }
            _cached[key] = _recency.AddFirst(new Entry(key, chunk));
            _bytes += chunk.Samples.Length * sizeof(short);
            while (_bytes > _budgetBytes && _recency.Count > 1 && _recency.Last is { } oldest)
            {
                _recency.RemoveLast();
                _cached.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Chunk.Samples.Length * sizeof(short);
            }
        }
    }

    private sealed record Chunk(short[] Samples, int Frames);

    private sealed record Entry((TrackId Track, long Index) Key, Chunk Chunk);

    private sealed class Decoder(ISampleSource source)
    {
        public ISampleSource Source { get; } = source;

        public long Position { get; set; }

        public float[] Scratch { get; } = new float[ChunkFrames * Channels];
    }
}
