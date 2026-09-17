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

    internal static void EngineFlush(IntPtr engine) => aria_engine_flush(engine);

    internal static long EnginePlayedFrames(IntPtr engine) => aria_engine_played_frames(engine);

    internal static bool DecoderOpenUsesWide => OperatingSystem.IsWindows();

    internal static int DecoderOpen(string path, int sampleRate, int channels, out IntPtr decoder)
        => DecoderOpen(path, sampleRate, channels, DecoderOpenUsesWide, out decoder);

    internal static int DecoderOpen(string path, int sampleRate, int channels, bool useWide, out IntPtr decoder)
        => useWide
            ? aria_decoder_open_w(path, sampleRate, channels, out decoder)
            : aria_decoder_open(path, sampleRate, channels, out decoder);

    internal static int DecoderRead(IntPtr decoder, IntPtr destination, int frameCount)
        => aria_decoder_read(decoder, destination, frameCount);

    internal static int DecoderSeek(IntPtr decoder, long frameIndex) => aria_decoder_seek(decoder, frameIndex);

    internal static void DecoderClose(IntPtr decoder) => aria_decoder_close(decoder);

    internal static int OutputDeviceIdSize() => aria_device_id_size();

    internal static int OutputCount() => aria_output_count();

    internal static int OutputInfo(int index, byte[] name, int nameCapacity, byte[] id, int idLength, out int isDefault)
        => aria_output_info(index, name, nameCapacity, id, idLength, out isDefault);

    internal static int EngineCreateOnDevice(int sampleRate, int channels, int blockSizeFrames, int backend, byte[] id, int idLength, out IntPtr engine)
        => aria_engine_create_on_device(sampleRate, channels, blockSizeFrames, backend, id, idLength, out engine);

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
    private static extern void aria_engine_flush(IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern long aria_engine_played_frames(IntPtr engine);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_decoder_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int sampleRate, int channels, out IntPtr decoder);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl, EntryPoint = "aria_decoder_open_w")]
    private static extern int aria_decoder_open_w([MarshalAs(UnmanagedType.LPWStr)] string path, int sampleRate, int channels, out IntPtr decoder);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_decoder_read(IntPtr decoder, IntPtr destination, int frameCount);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_decoder_seek(IntPtr decoder, long frameIndex);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern void aria_decoder_close(IntPtr decoder);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_device_id_size();

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_output_count();

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_output_info(int index, byte[] name, int nameCapacity, byte[] id, int idLength, out int isDefault);

    [DllImport("aria_shim", CallingConvention = CallingConvention.Cdecl)]
    private static extern int aria_engine_create_on_device(int sampleRate, int channels, int blockSizeFrames, int backend, byte[] id, int idLength, out IntPtr engine);
}
