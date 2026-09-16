namespace Aria.App.Services;

using System.IO.Compression;

public static class ProjectFolder
{
    public const string FolderName = ".aria";

    public const string ProjectFileName = "project.json";

    public const string ScriptsDir = "scripts";

    public static string ProjectPath(string dir) => Path.Combine(dir, FolderName, ProjectFileName);

    public static string ScriptsPath(string dir) => Path.Combine(dir, FolderName, ScriptsDir);

    public static bool HasProject(string dir) => File.Exists(ProjectPath(dir));

    public const string ZipAudioDir = "audio";

    public static void BuildZip(string zipPath, string projectJson, IEnumerable<string> audioFiles)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var jsonEntry = archive.CreateEntry(FolderName + "/" + ProjectFileName);
        using (var writer = new StreamWriter(jsonEntry.Open()))
        {
            writer.Write(projectJson);
        }
        foreach (var file in audioFiles.Distinct(StringComparer.Ordinal))
        {
            archive.CreateEntryFromFile(file, ZipAudioDir + "/" + Path.GetFileName(file));
        }
    }

    public static string ExtractProject(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (entry.FullName.EndsWith('/') || Path.IsPathRooted(name) || name.Split(Path.DirectorySeparatorChar).Contains(".."))
            {
                continue;
            }
            var dest = Path.Combine(destDir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
        return File.ReadAllText(ProjectPath(destDir));
    }

    public static string SafeFileName(string name)
    {
        var cleaned = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(cleaned) ? "Без названия" : cleaned;
    }
}
