namespace Aria.Remote;

using System.IO;
using Microsoft.AspNetCore.Http;

public static class RemoteStaticFiles
{
    private const string ResourcePrefix = "Aria.Remote.RemoteClient.";
    private const string CacheControl = "no-cache";

    private static readonly HashSet<string> Manifest = [.. typeof(RemoteStaticFiles).Assembly.GetManifestResourceNames()];

    public static Task<IResult> ServeIndex() => Serve("index.html");

    public static Task<IResult> ServeAsset(string path) => Serve(path);

    public static Task<IResult> Serve(string path)
    {
        var resource = Resolve(path);
        if (resource is null)
        {
            return Task.FromResult(Results.NotFound());
        }
        using var stream = typeof(RemoteStaticFiles).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return Task.FromResult(Results.NotFound());
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Task.FromResult<IResult>(new EmbeddedFile(buffer.ToArray(), ContentType(resource)));
    }

    private static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        if (path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }
        var candidate = ResourcePrefix + string.Join('.', segments);
        return Manifest.Contains(candidate) ? candidate : null;
    }

    private static string ContentType(string resource)
    {
        var extension = Path.GetExtension(resource).ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".json" => "application/json",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
    }

    private sealed class EmbeddedFile(byte[] content, string contentType) : IResult
    {
        public Task ExecuteAsync(HttpContext context)
        {
            context.Response.ContentType = contentType;
            context.Response.Headers.CacheControl = CacheControl;
            return context.Response.Body.WriteAsync(content).AsTask();
        }
    }
}
