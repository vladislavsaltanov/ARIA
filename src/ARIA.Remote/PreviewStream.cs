namespace Aria.Remote;

using System.Buffers;
using Aria.Audio;
using Aria.Core.Playback;
using Microsoft.AspNetCore.Http;

internal sealed class PreviewStream(PreviewTap? tap) : IResult
{
    private const int SampleRate = 48000;
    private const int ChunkFrames = 1024;
    private const int IdleDelayMs = 5;

    public async Task ExecuteAsync(HttpContext context)
    {
        var channels = tap?.Channels ?? 2;
        var header = new byte[44];
        WriteHeader(header, channels);
        context.Response.ContentType = "audio/x-wav";
        await context.Response.Body.WriteAsync(header, context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        if (tap is null)
        {
            return;
        }
        var reader = tap.Subscribe();
        var rented = ArrayPool<float>.Shared.Rent(ChunkFrames * channels);
        var pcm = new byte[ChunkFrames * channels * 2];
        try
        {
            while (!context.RequestAborted.IsCancellationRequested)
            {
                var read = reader.Read(rented.AsSpan(0, ChunkFrames * channels));
                if (read == 0)
                {
                    await Task.Delay(IdleDelayMs, context.RequestAborted);
                    continue;
                }
                for (var i = 0; i < read; i++)
                {
                    var sample = Math.Clamp((int)Math.Round(rented[i] * short.MaxValue), short.MinValue, short.MaxValue);
                    pcm[i * 2] = (byte)sample;
                    pcm[i * 2 + 1] = (byte)(sample >> 8);
                }
                await context.Response.Body.WriteAsync(pcm.AsMemory(0, read * 2), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    private static void WriteHeader(byte[] header, int channels)
    {
        WriteAscii(header, 0, "RIFF");
        WriteInt32(header, 4, unchecked(int.MaxValue - 8));
        WriteAscii(header, 8, "WAVE");
        WriteAscii(header, 12, "fmt ");
        WriteInt32(header, 16, 16);
        WriteInt16(header, 20, 1);
        WriteInt16(header, 22, (short)channels);
        WriteInt32(header, 24, SampleRate);
        WriteInt32(header, 28, SampleRate * channels * 2);
        WriteInt16(header, 32, (short)(channels * 2));
        WriteInt16(header, 34, 16);
        WriteAscii(header, 36, "data");
        WriteInt32(header, 40, int.MaxValue);
    }

    private static void WriteAscii(byte[] target, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            target[offset + i] = (byte)text[i];
        }
    }

    private static void WriteInt16(byte[] target, int offset, short value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }
}
