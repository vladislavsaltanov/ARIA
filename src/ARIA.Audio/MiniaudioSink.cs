namespace Aria.Audio;

using System.Runtime.InteropServices;
using Aria.Audio.Native;

public sealed class MiniaudioSink : IAudioSink, IOutputClockSource, IDisposable
{
    private readonly float[] _scratch;
    private readonly GCHandle _scratchHandle;
    private readonly int _blockSizeFrames;
    private readonly TimeSpan _writeTimeout;
    private readonly Func<int> _space;
    private readonly FrameWriter _writer;
    private readonly object _clockGate = new();
    private IntPtr _engine;

    public static readonly TimeSpan DefaultWriteTimeout = TimeSpan.FromMilliseconds(20);

    public MiniaudioSink(int sampleRate, int channels, int blockSizeFrames, int backend = 0, byte[]? deviceId = null, TimeSpan? writeTimeout = null)
    {
        SampleRate = sampleRate;
        Channels = channels;
        _blockSizeFrames = blockSizeFrames;
        _writeTimeout = writeTimeout ?? DefaultWriteTimeout;
        _space = () => AriaShim.EngineSpace(_engine);
        _writer = WriteChunk;
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

    public int Write(ReadOnlySpan<float> samples) =>
        RingWriter.Write(samples, Channels, _blockSizeFrames, _writeTimeout, _space, _writer);

    private int WriteChunk(ReadOnlySpan<float> chunk, int frames)
    {
        chunk.CopyTo(_scratch);
        return AriaShim.EngineWrite(_engine, _scratchHandle.AddrOfPinnedObject(), frames);
    }

    public void Flush() => AriaShim.EngineFlush(_engine);

    public int DeviceBufferFrames
    {
        get
        {
            lock (_clockGate)
            {
                return _engine == IntPtr.Zero ? 0 : AriaShim.EngineDeviceBufferFrames(_engine);
            }
        }
    }

    public bool TryReadStamp(out long frame, out long nativeNs, out long flushGeneration)
    {
        lock (_clockGate)
        {
            if (_engine == IntPtr.Zero)
            {
                frame = 0;
                nativeNs = 0;
                flushGeneration = 0;
                return false;
            }
            return AriaShim.EngineClock(_engine, out frame, out nativeNs, out flushGeneration) == 0;
        }
    }

    public long NativeNowNs() => AriaShim.ClockNowNs();

    public void Dispose()
    {
        lock (_clockGate)
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
}
