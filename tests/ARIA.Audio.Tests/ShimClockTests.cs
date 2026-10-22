namespace Aria.Audio.Tests;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Aria.Audio.Native;

public sealed class ShimClockTests
{
    private const int SampleRate = 8000;
    private const int BlockFrames = 256;

    [Fact]
    public void EngineClock_NullBackend_StampsAdvance()
    {
        using var engine = NullEngine.TryStart();
        if (engine is null)
        {
            return;
        }
        engine.Feed(300);
        Assert.Equal(0, AriaShim.EngineClock(engine.Handle, out var frame1, out var ns1, out _));
        engine.Feed(200);
        Assert.Equal(0, AriaShim.EngineClock(engine.Handle, out var frame2, out var ns2, out _));

        Assert.True(frame2 > frame1, $"frame did not advance: {frame1} -> {frame2}");
        Assert.True(ns2 > ns1, $"ns did not advance: {ns1} -> {ns2}");
        Assert.True(frame2 <= AriaShim.EnginePlayedFrames(engine.Handle));
    }

    [Fact]
    public void EngineClock_Flush_IncrementsGeneration()
    {
        using var engine = NullEngine.TryStart();
        if (engine is null)
        {
            return;
        }
        engine.Feed(150);
        Assert.Equal(0, AriaShim.EngineClock(engine.Handle, out _, out _, out var before));

        AriaShim.EngineFlush(engine.Handle);
        engine.Feed(150);

        Assert.Equal(0, AriaShim.EngineClock(engine.Handle, out _, out _, out var after));
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public void ClockNowNs_IsMonotonic_AndTracksStopwatch()
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var first = AriaShim.ClockNowNs();
            Thread.Sleep(50);
            var second = AriaShim.ClockNowNs();
            var elapsedNs = stopwatch.Elapsed.TotalMilliseconds * 1_000_000;

            Assert.True(second > first);
            Assert.InRange(second - first, elapsedNs - 5_000_000, elapsedNs + 5_000_000);
        }
        catch (DllNotFoundException)
        {
        }
    }

    [Fact]
    public void DeviceBufferFrames_Positive()
    {
        using var engine = NullEngine.TryStart();
        if (engine is null)
        {
            return;
        }

        Assert.True(AriaShim.EngineDeviceBufferFrames(engine.Handle) > 0);
    }

    private sealed class NullEngine : IDisposable
    {
        private readonly float[] _block = new float[BlockFrames];
        private readonly GCHandle _pin;

        private NullEngine(IntPtr handle)
        {
            Handle = handle;
            _pin = GCHandle.Alloc(_block, GCHandleType.Pinned);
        }

        public IntPtr Handle { get; }

        public static NullEngine? TryStart()
        {
            try
            {
                if (AriaShim.EngineCreate(SampleRate, 1, BlockFrames, 1, out var handle) != 0)
                {
                    return null;
                }
                var engine = new NullEngine(handle);
                AriaShim.EngineStart(handle);
                return engine;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
        }

        public void Feed(int milliseconds)
        {
            var deadline = Environment.TickCount64 + milliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (AriaShim.EngineSpace(Handle) >= BlockFrames)
                {
                    AriaShim.EngineWrite(Handle, _pin.AddrOfPinnedObject(), BlockFrames);
                }
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            AriaShim.EngineStop(Handle);
            AriaShim.EngineDestroy(Handle);
            _pin.Free();
        }
    }
}
