namespace Aria.Audio.Tests;

using System.Runtime.InteropServices;
using System.Text;
using Aria.Audio.Native;

public sealed class UnicodePathDecoderTests : IDisposable
{
    private const int SampleRate = 8000;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "aria-unicode-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Open_CyrillicDirAndNumeroName_OpensAndReadsAudio()
    {
        var directory = Path.Combine(_directory, "Музыка", "Фоновая музыка");
        var path = TestWav.WriteSine(directory, "Трек №1.wav", SampleRate, 2, 0.5, 440.0, 0.5);
        var factory = new MiniaudioSourceFactory(SampleRate, 2);
        var source = factory.Open(path, TimeSpan.Zero, null);
        Assert.NotNull(source);
        using var scope = (IDisposable)source;
        var buffer = new float[1024 * source.Channels];
        Assert.True(source.ReadFrames(buffer) > 0);
        Assert.True(factory.TryOpen(path, TimeSpan.Zero, null, out var reopened, out var fault));
        Assert.NotNull(reopened);
        ((IDisposable)reopened).Dispose();
    }

    [Fact]
    public void DecoderOpen_UsesWideOnlyOnWindows()
    {
        Assert.Equal(OperatingSystem.IsWindows(), AriaShim.DecoderOpenUsesWide);
    }

    [Fact]
    public void WideExport_OpensCyrillicFile()
    {
        var name = OperatingSystem.IsWindows() ? "Трек №2.wav" : "wide-entry.wav";
        var path = TestWav.WriteSine(_directory, name, SampleRate, 2, 0.5, 440.0, 0.5);
        var library = LoadShimHandle();
        try
        {
            Assert.True(NativeLibrary.TryGetExport(library, "aria_decoder_open_w", out var openPtr), "wide export missing");
            Assert.True(NativeLibrary.TryGetExport(library, "aria_decoder_close", out var closePtr), "close export missing");
            var open = Marshal.GetDelegateForFunctionPointer<WideOpenDelegate>(openPtr);
            var close = Marshal.GetDelegateForFunctionPointer<RawCloseDelegate>(closePtr);
            var encoded = (OperatingSystem.IsWindows() ? Encoding.Unicode : Encoding.UTF32).GetBytes(path + "\0");
            var nativePath = Marshal.AllocHGlobal(encoded.Length);
            try
            {
                Marshal.Copy(encoded, 0, nativePath, encoded.Length);
                Assert.Equal(0, open(nativePath, SampleRate, 2, out var decoder));
                Assert.NotEqual(IntPtr.Zero, decoder);
                close(decoder);
            }
            finally
            {
                Marshal.FreeHGlobal(nativePath);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void DecoderOpen_ExplicitNarrow_OpensCyrillicFile()
    {
        var path = TestWav.WriteSine(_directory, "Маршрут-narrow.wav", SampleRate, 2, 0.5, 440.0, 0.5);
        var opened = AriaShim.DecoderOpen(path, SampleRate, 2, useWide: false, out var decoder);
        Assert.Equal(0, opened);
        Assert.NotEqual(IntPtr.Zero, decoder);
        AriaShim.DecoderClose(decoder);
    }

    private static IntPtr LoadShimHandle()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(MiniaudioSourceFactory).Assembly.Location);
        Assert.NotNull(assemblyDirectory);
        var fileName = OperatingSystem.IsWindows() ? "aria_shim.dll" : OperatingSystem.IsMacOS() ? "libaria_shim.dylib" : "libaria_shim.so";
        return NativeLibrary.Load(Path.Combine(assemblyDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WideOpenDelegate(IntPtr path, int sampleRate, int channels, out IntPtr decoder);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RawCloseDelegate(IntPtr decoder);
}
