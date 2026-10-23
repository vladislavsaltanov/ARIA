namespace Aria.Remote.Monitor;

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Aria.Core.Model;
using Aria.Core.Playback;
using Microsoft.AspNetCore.Http;

internal static class PcmEndpoint
{
    public const int MaxFrames = 240000;

    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(2);

    public static async Task HandleAsync(HttpContext context, RemoteRole role, PcmCache cache, IMonitorSources sources, Func<ImmutableHashSet<TrackId>> allowed, CancellationToken hostLifetime)
    {
        var query = context.Request.Query;
        if (!Guid.TryParse(query["track"], out var guid)
            || !long.TryParse(query["from"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var from) || from < 0
            || !int.TryParse(query["frames"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames) || frames is < 1 or > MaxFrames)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var track = new TrackId(guid);
        if (role == RemoteRole.Listen && !allowed().Contains(track))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (sources.Find(track) is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (cache.IsUndecodable(track))
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, hostLifetime);
        timeout.CancelAfter(DecodeTimeout);
        PcmSlice slice;
        try
        {
            slice = await cache.ReadAsync(track, from, frames, timeout.Token);
        }
        catch (InvalidDataException)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return;
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/octet-stream";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Aria-Frames"] = slice.Frames.ToString(CultureInfo.InvariantCulture);
        if (slice.TotalFrames is { } total)
        {
            context.Response.Headers["X-Aria-Total-Frames"] = total.ToString(CultureInfo.InvariantCulture);
        }
        await context.Response.Body.WriteAsync(MemoryMarshal.AsBytes(slice.Samples.AsSpan()).ToArray(), context.RequestAborted);
    }
}
