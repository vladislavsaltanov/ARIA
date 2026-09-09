namespace Aria.Audio;

using System.Runtime.InteropServices;

public sealed class SampleRing
{
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedLong
    {
        [FieldOffset(64)]
        public long Value;
    }

    private readonly float[] _buffer;
    private readonly int _mask;
    private readonly int _channels;
    private PaddedLong _head;
    private PaddedLong _tail;

    public SampleRing(int capacityFrames, int channels)
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

    public int Count
    {
        get
        {
            var head = Volatile.Read(ref _head.Value);
            var tail = Volatile.Read(ref _tail.Value);
            return (int)(head - tail) * _channels;
        }
    }

    public int Write(ReadOnlySpan<float> data)
    {
        var head = Volatile.Read(ref _head.Value);
        var tail = Volatile.Read(ref _tail.Value);
        var freeFrames = CapacityFrames - (int)(head - tail);
        var frames = Math.Min(data.Length / _channels, freeFrames);
        if (frames <= 0)
        {
            return 0;
        }
        var writeIndex = (int)(head & _mask);
        var first = Math.Min(frames, CapacityFrames - writeIndex);
        CopyFrames(data, 0, writeIndex * _channels, first);
        CopyFrames(data, first, 0, frames - first);
        Volatile.Write(ref _head.Value, head + frames);
        return frames * _channels;
    }

    public int Read(Span<float> data)
    {
        var head = Volatile.Read(ref _head.Value);
        var tail = Volatile.Read(ref _tail.Value);
        var availableFrames = (int)(head - tail);
        var frames = Math.Min(data.Length / _channels, availableFrames);
        if (frames <= 0)
        {
            return 0;
        }
        var readIndex = (int)(tail & _mask);
        var first = Math.Min(frames, CapacityFrames - readIndex);
        CopyTo(data, 0, readIndex * _channels, first);
        CopyTo(data, first, 0, frames - first);
        Volatile.Write(ref _tail.Value, tail + frames);
        return frames * _channels;
    }

    private void CopyFrames(ReadOnlySpan<float> source, int sourceFrame, int destinationOffset, int frames)
    {
        if (frames <= 0)
        {
            return;
        }
        source.Slice(sourceFrame * _channels, frames * _channels).CopyTo(_buffer.AsSpan(destinationOffset));
    }

    private void CopyTo(Span<float> destination, int destinationFrame, int sourceOffset, int frames)
    {
        if (frames <= 0)
        {
            return;
        }
        _buffer.AsSpan(sourceOffset, frames * _channels).CopyTo(destination.Slice(destinationFrame * _channels));
    }
}
