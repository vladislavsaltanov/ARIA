namespace Aria.Audio;

using System.Runtime.InteropServices;
using Aria.Audio.Native;

public sealed class MiniaudioSink : IAudioSink, IDisposable
{
    private readonly float[] _scratch;
    private readonly GCHandle _scratchHandle;
    private readonly int _blockSizeFrames;
    private IntPtr _engine;

    public MiniaudioSink(int sampleRate, int channels, int blockSizeFrames, int backend = 0, byte[]? deviceId = null)
    {
        SampleRate = sampleRate;
        Channels = channels;
        _blockSizeFrames = blockSizeFrames;
        _scratch = new float[Math.Max(1, blockSizeFrames * channels)];
        _scratchHandle = GCHandle.Alloc(_scratch, GCHandleType.Pinned);
        IntPtr engine;
        var created = deviceId is null
            ? AriaShim.EngineCreate(sampleRate, channels, blockSizeFrames, backend, out engine)
            : AriaShim.EngineCreateOnDevice(sampleRate, channels, blockSizeFrames, backend, deviceId, deviceId.Length, out engine);
        if (created != 0)
        {
            _scratchHandle.Free();
            throw new InvalidOperationException($"aria_engine_create failed with code {created}");
        }
        _engine = engine;
        var started = AriaShim.EngineStart(_engine);
        if (started != 0)
        {
            AriaShim.EngineDestroy(_engine);
            _engine = IntPtr.Zero;
            _scratchHandle.Free();
            throw new InvalidOperationException($"aria_engine_start failed with code {started}");
        }
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public long PlayedFrames => AriaShim.EnginePlayedFrames(_engine);

    public int Write(ReadOnlySpan<float> samples)
    {
        var total = 0;
        while (total < samples.Length)
        {
            var space = AriaShim.EngineSpace(_engine);
            var remaining = samples.Length - total;
            var acceptedFrames = Math.Min(Math.Min(remaining / Channels, space), _blockSizeFrames);
            if (acceptedFrames <= 0)
            {
                Thread.Sleep(1);
                continue;
            }
            var acceptedSamples = acceptedFrames * Channels;
            samples.Slice(total, acceptedSamples).CopyTo(_scratch);
            var writtenFrames = AriaShim.EngineWrite(_engine, _scratchHandle.AddrOfPinnedObject(), acceptedFrames);
            total += writtenFrames * Channels;
        }
        return total;
    }

    public void Flush() => AriaShim.EngineFlush(_engine);

    public void Dispose()
    {
        if (_engine == IntPtr.Zero)
        {
            return;
        }
        AriaShim.EngineStop(_engine);
        AriaShim.EngineDestroy(_engine);
        _engine = IntPtr.Zero;
        _scratchHandle.Free();
    }
}
