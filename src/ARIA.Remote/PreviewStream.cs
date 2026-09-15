namespace Aria.Remote;

using System.Buffers;
using Aria.Audio;
using Microsoft.AspNetCore.Http;

internal sealed class PreviewCapture(SampleRing? tap) : IResult
{
    private const int SampleRate = 48000;
    private const int MaxFrames = 48000;

    public Task ExecuteAsync(HttpContext context)
    {
        var channels = tap?.Channels ?? 2;
        var rented = ArrayPool<float>.Shared.Rent(MaxFrames * channels);
        try
        {
            var read = tap?.Read(rented.AsSpan(0, MaxFrames * channels)) ?? 0;
            var frames = read / channels;
            var pcm = new byte[frames * channels * 2];
            for (var i = 0; i < frames * channels; i++)
            {
                var sample = Math.Clamp((int)Math.Round(rented[i] * short.MaxValue), short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)sample;
                pcm[i * 2 + 1] = (byte)(sample >> 8);
            }
            var header = new byte[44];
            WriteHeader(header, channels, frames);
            context.Response.ContentType = "audio/x-wav";
            context.Response.ContentLength = header.Length + pcm.Length;
            var body = context.Response.Body;
            return WriteBodyAsync(body, header, pcm);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    private static async Task WriteBodyAsync(Stream body, byte[] header, byte[] pcm)
    {
        await body.WriteAsync(header);
        if (pcm.Length > 0)
        {
            await body.WriteAsync(pcm);
        }
    }

    private static void WriteHeader(byte[] header, int channels, int frames)
    {
        var dataBytes = frames * channels * 2;
        WriteAscii(header, 0, "RIFF");
        WriteInt32(header, 4, 36 + dataBytes);
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
        WriteInt32(header, 40, dataBytes);
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
