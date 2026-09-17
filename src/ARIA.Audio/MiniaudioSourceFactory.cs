namespace Aria.Audio;

using System.Runtime.InteropServices;
using Aria.Audio.Native;
using Aria.Core.Playback;

public sealed class MiniaudioSourceFactory : ISourceFactory
{
    private const int ChunkFrames = 4096;

    private readonly int _outputSampleRate;
    private readonly int _outputChannels;

    public MiniaudioSourceFactory(int outputSampleRate, int outputChannels)
    {
        _outputSampleRate = outputSampleRate;
        _outputChannels = outputChannels;
    }

    public ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut)
    {
        var opened = AriaShim.DecoderOpen(filePath, _outputSampleRate, _outputChannels, out var decoder);
        if (opened != 0)
        {
            return null;
        }
        return new DecoderSource(decoder, _outputChannels, _outputSampleRate, cueIn + GapDelay(filePath), cueOut);
    }

    private static TimeSpan GapDelay(string filePath) =>
        Mp3Gapless.TryRead(filePath, out var info) ? info.Delay() : TimeSpan.Zero;

    public bool TryOpen(string filePath, TimeSpan cueIn, TimeSpan? cueOut, out ISampleSource? source, out SourceOpenFault fault)
    {
        source = null;
        fault = SourceOpenFault.Undecodable;
        if (AriaShim.DecoderOpen(filePath, _outputSampleRate, _outputChannels, out var decoder) != 0)
        {
            fault = File.Exists(filePath) ? SourceOpenFault.Undecodable : SourceOpenFault.Missing;
            return false;
        }
        source = new DecoderSource(decoder, _outputChannels, _outputSampleRate, cueIn + GapDelay(filePath), cueOut);
        fault = SourceOpenFault.Unknown;
        return true;
    }

    private sealed class DecoderSource : ISampleSource, IDisposable
    {
        private readonly float[] _scratch;
        private readonly GCHandle _scratchHandle;
        private readonly IntPtr _scratchPointer;
        private readonly long _skipFrames;
        private readonly long _maxFrames;
        private long _consumed;
        private long _delivered;
        private IntPtr _handle;

        public DecoderSource(IntPtr decoder, int channels, int sampleRate, TimeSpan cueIn, TimeSpan? cueOut)
        {
            _handle = decoder;
            Channels = channels;
            SampleRate = sampleRate;
            _scratch = new float[ChunkFrames * channels];
            _scratchHandle = GCHandle.Alloc(_scratch, GCHandleType.Pinned);
            _scratchPointer = _scratchHandle.AddrOfPinnedObject();
            _skipFrames = cueIn.Ticks * sampleRate / TimeSpan.TicksPerSecond;
            _maxFrames = cueOut is { } cueOutTime
                ? Math.Max(0, (cueOutTime - cueIn).Ticks * sampleRate / TimeSpan.TicksPerSecond)
                : long.MaxValue;
        }

        public int Channels { get; }

        public int SampleRate { get; }

        public int ReadFrames(Span<float> destination)
        {
            var request = destination.Length / Channels;
            if (request <= 0 || _delivered >= _maxFrames || !SkipPendingCueIn())
            {
                return 0;
            }
            var frames = (int)Math.Min(Math.Min(request, ChunkFrames), _maxFrames - _delivered);
            var read = AriaShim.DecoderRead(_handle, _scratchPointer, frames);
            if (read <= 0)
            {
                return 0;
            }
            _consumed += read;
            _delivered += read;
            _scratch.AsSpan(0, read * Channels).CopyTo(destination);
            return read;
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }
            AriaShim.DecoderClose(_handle);
            _handle = IntPtr.Zero;
            _scratchHandle.Free();
        }

        public void Seek(long frameIndex)
        {
            if (_handle == IntPtr.Zero || frameIndex < 0 || frameIndex > _maxFrames)
            {
                return;
            }
            var fileFrame = _skipFrames + frameIndex;
            if (AriaShim.DecoderSeek(_handle, fileFrame) != 0)
            {
                return;
            }
            _consumed = fileFrame;
            _delivered = frameIndex;
        }

        private bool SkipPendingCueIn()
        {
            while (_consumed < _skipFrames)
            {
                var chunk = (int)Math.Min(_skipFrames - _consumed, ChunkFrames);
                var read = AriaShim.DecoderRead(_handle, _scratchPointer, chunk);
                if (read <= 0 || read < chunk)
                {
                    return false;
                }
                _consumed += read;
            }
            return true;
        }
    }
}
