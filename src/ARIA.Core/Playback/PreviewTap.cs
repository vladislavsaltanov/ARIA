namespace Aria.Core.Playback;

public sealed class PreviewTap
{
    private readonly float[] _buffer;
    private readonly int _mask;
    private readonly int _channels;
    private long _head;

    public PreviewTap(int capacityFrames, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        var capacity = 1;
        while (capacity < capacityFrames)
        {
            capacity <<= 1;
        }
        _buffer = new float[capacity * channels];
        _mask = capacity - 1;
        _channels = channels;
    }

    public int CapacityFrames => _mask + 1;

    public int Channels => _channels;

    public long Head => Volatile.Read(ref _head);

    public void Publish(ReadOnlySpan<float> data)
    {
        var frames = data.Length / _channels;
        if (frames <= 0)
        {
            return;
        }
        var head = Volatile.Read(ref _head);
        if (frames > CapacityFrames)
        {
            var skip = frames - CapacityFrames;
            data = data.Slice(skip * _channels);
            head += skip;
            frames = CapacityFrames;
        }
        var writeIndex = (int)(head & _mask);
        var first = Math.Min(frames, CapacityFrames - writeIndex);
        CopyIn(data, 0, writeIndex, first);
        CopyIn(data, first, 0, frames - first);
        Volatile.Write(ref _head, head + frames);
    }

    public PreviewReader Subscribe() => new(this, Volatile.Read(ref _head));

    internal int ReadFrom(ref long cursor, Span<float> target)
    {
        var head = Volatile.Read(ref _head);
        var oldest = Math.Max(0, head - CapacityFrames);
        if (cursor < oldest)
        {
            cursor = oldest;
        }
        var frames = (int)Math.Min(target.Length / _channels, head - cursor);
        if (frames <= 0)
        {
            return 0;
        }
        var readIndex = (int)(cursor & _mask);
        var first = Math.Min(frames, CapacityFrames - readIndex);
        CopyOut(cursor, target, 0, first);
        CopyOut(cursor + first, target, first, frames - first);
        cursor += frames;
        return frames * _channels;
    }

    private void CopyIn(ReadOnlySpan<float> source, int sourceFrame, int destinationFrame, int frames)
    {
        if (frames <= 0)
        {
            return;
        }
        source.Slice(sourceFrame * _channels, frames * _channels).CopyTo(_buffer.AsSpan(destinationFrame * _channels));
    }

    private void CopyOut(long cursor, Span<float> target, int targetFrame, int frames)
    {
        if (frames <= 0)
        {
            return;
        }
        var sourceFrame = (int)(cursor & _mask);
        _buffer.AsSpan(sourceFrame * _channels, frames * _channels).CopyTo(target.Slice(targetFrame * _channels));
    }
}

public sealed class PreviewReader
{
    private readonly PreviewTap _tap;
    private long _cursor;

    internal PreviewReader(PreviewTap tap, long cursor)
    {
        _tap = tap;
        _cursor = cursor;
    }

    public int Read(Span<float> target) => _tap.ReadFrom(ref _cursor, target);

    public void Seek(long cursor) => Volatile.Write(ref _cursor, cursor);

    public void ResetToHead() => Volatile.Write(ref _cursor, _tap.Head);
}
