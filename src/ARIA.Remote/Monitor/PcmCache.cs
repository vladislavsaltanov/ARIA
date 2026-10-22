namespace Aria.Remote.Monitor;

using System.Collections.Concurrent;
using Aria.Audio;
using Aria.Core.Model;
using Aria.Core.Playback;

internal sealed record PcmSlice(short[] Samples, int Frames, long? TotalFrames);

internal sealed class PcmCache : IDisposable
{
    public const int ChunkFrames = 48000;

    private static readonly TimeSpan UndecodableRetry = TimeSpan.FromSeconds(5);

    private const int Channels = 2;

    private readonly ISourceFactory _factory;
    private readonly IMonitorSources _sources;
    private readonly long _budgetBytes;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<(Source Source, long Index), LinkedListNode<Entry>> _cached = [];
    private readonly LinkedList<Entry> _recency = [];
    private readonly Dictionary<(Source Source, long Index), TaskCompletionSource<Chunk>> _pending = [];
    private readonly Dictionary<Source, DateTimeOffset> _undecodable = [];
    private readonly Dictionary<Source, long> _totals = [];
    private readonly Dictionary<TrackId, Decoder> _decoders = [];
    private readonly BlockingCollection<(Source Source, long Index)> _queue = [];
    private readonly Thread _worker;
    private long _bytes;
    private int _decodedChunks;

    public PcmCache(ISourceFactory factory, IMonitorSources sources, long budgetBytes = 512L * 1024 * 1024, TimeProvider? time = null)
    {
        _factory = factory;
        _sources = sources;
        _budgetBytes = budgetBytes;
        _time = time ?? TimeProvider.System;
        _worker = new Thread(Work) { IsBackground = true, Name = "aria-pcm", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    internal int DecodedChunks => Volatile.Read(ref _decodedChunks);

    public bool IsUndecodable(TrackId track)
    {
        if (Resolve(track) is not { } source)
        {
            return false;
        }
        lock (_gate)
        {
            return IsMarked(source);
        }
    }

    public async Task<PcmSlice> ReadAsync(TrackId track, long fromFrame, int frames, CancellationToken cancellationToken)
    {
        var source = Resolve(track) ?? throw new InvalidDataException("undecodable");
        var samples = new short[frames * Channels];
        var filled = 0;
        var position = fromFrame;
        while (filled < frames)
        {
            var index = position / ChunkFrames;
            var chunk = await GetChunkAsync(source, index, cancellationToken);
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
            total = _totals.TryGetValue(source, out var known) ? known : null;
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

    private Source? Resolve(TrackId track) =>
        _sources.Find(track) is { } found ? new Source(track, found.FilePath) : null;

    private bool IsMarked(Source source)
    {
        if (!_undecodable.TryGetValue(source, out var since))
        {
            return false;
        }
        if (_time.GetUtcNow() - since < UndecodableRetry)
        {
            return true;
        }
        _undecodable.Remove(source);
        return false;
    }

    private Task<Chunk> GetChunkAsync(Source source, long index, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (IsMarked(source))
            {
                throw new InvalidDataException("undecodable");
            }
            var key = (source, index);
            if (_cached.TryGetValue(key, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return Task.FromResult(node.Value.Chunk);
            }
            if (_totals.TryGetValue(source, out var total) && index * ChunkFrames >= total)
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
                var chunk = Decode(key.Source, key.Index);
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

    private Chunk Decode(Source target, long index)
    {
        if (_decoders.TryGetValue(target.Track, out var decoder) && decoder.Path != target.Path)
        {
            (decoder.Source as IDisposable)?.Dispose();
            _decoders.Remove(target.Track);
            decoder = null;
        }
        if (decoder is null)
        {
            var source = _factory.Open(target.Path, TimeSpan.Zero, null);
            if (source is null || source.Channels != Channels)
            {
                (source as IDisposable)?.Dispose();
                lock (_gate)
                {
                    _undecodable[target] = _time.GetUtcNow();
                }
                throw new InvalidDataException("undecodable");
            }
            decoder = new Decoder(source, target.Path);
            _decoders[target.Track] = decoder;
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
                _totals[target] = start + total;
            }
        }
        return new Chunk(samples, total);
    }

    private void Store((Source Source, long Index) key, Chunk chunk)
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

    private sealed record Entry((Source Source, long Index) Key, Chunk Chunk);

    private readonly record struct Source(TrackId Track, string Path);

    private sealed class Decoder(ISampleSource source, string path)
    {
        public ISampleSource Source { get; } = source;

        public string Path { get; } = path;

        public long Position { get; set; }

        public float[] Scratch { get; } = new float[ChunkFrames * Channels];
    }
}
