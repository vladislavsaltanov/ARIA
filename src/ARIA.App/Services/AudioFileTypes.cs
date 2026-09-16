namespace Aria.App.Services;

using System.Collections.Immutable;
using Avalonia.Platform.Storage;

internal static class AudioFileTypes
{
    internal static readonly ImmutableArray<string> Extensions = [".wav", ".flac", ".mp3", ".ogg"];

    internal static FilePickerFileType Filter { get; } = new("Аудио") { Patterns = [.. Extensions.Select(extension => "*" + extension)] };
}
