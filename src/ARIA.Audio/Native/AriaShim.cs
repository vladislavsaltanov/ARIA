namespace Aria.Audio.Native;

using System.Reflection;
using System.Runtime.InteropServices;

internal static class AriaShim
{
    private const string LibraryName = "aria_shim";

    static AriaShim()
    {
        NativeLibrary.SetDllImportResolver(typeof(AriaShim).Assembly, Resolve);
    }

    internal static int EngineCreate(int sampleRate, int channels, int blockSizeFrames, int backend, out IntPtr engine)
        => aria_engine_create(sampleRate, channels, blockSizeFrames, backend, out engine);

    internal static int EngineStart(IntPtr engine) => aria_engine_start(engine);

    internal static int EngineStop(IntPtr engine) => aria_engine_stop(engine);

    internal static void EngineDestroy(IntPtr engine) => aria_engine_destroy(engine);

    internal static int EngineWrite(IntPtr engine, IntPtr data, int frameCount)
        => aria_engine_write(engine, data, frameCount);

    internal static int EngineSpace(IntPtr engine) => aria_engine_space(engine);

    internal static void EngineSetMasterGain(IntPtr engine, float gain)
        => aria_engine_set_master_gain(engine, gain);

    internal static void EngineFlush(IntPtr engine) => aria_engine_flush(engine);

    internal static long EnginePlayedFrames(IntPtr engine) => aria_engine_played_frames(engine);

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
        if (assemblyDirectory is not null)
        {
            var fileName = NativeFileName();
            if (NativeLibrary.TryLoad(Path.Combine(assemblyDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName), out var handle))
            {
                return handle;
            }
            if (NativeLibrary.TryLoad(Path.Combine(assemblyDirectory, fileName), out handle))
            {
                return handle;
            }
        }
        return NativeLibrary.TryLoad(libraryName, out var fallback) ? fallback : IntPtr.Zero;
    }

    private static string NativeFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "aria_shim.dll";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "libaria_shim.dylib";
        }
        return "libaria_shim.so";
    }

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_engine_create(int sampleRate, int channels, int blockSizeFrames, int backend, out IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_engine_start(IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_engine_stop(IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern void aria_engine_destroy(IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_engine_write(IntPtr engine, IntPtr data, int frameCount);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_engine_space(IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern void aria_engine_set_master_gain(IntPtr engine, float gain);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern void aria_engine_flush(IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern long aria_engine_played_frames(IntPtr engine);
}
