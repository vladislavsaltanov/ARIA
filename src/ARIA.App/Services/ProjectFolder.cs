namespace Aria.App.Services;

public static class ProjectFolder
{
    public const string FolderName = ".aria";

    public const string ProjectFileName = "project.json";

    public const string ScriptsDir = "scripts";

    public static string ProjectPath(string dir) => Path.Combine(dir, FolderName, ProjectFileName);

    public static string ScriptsPath(string dir) => Path.Combine(dir, FolderName, ScriptsDir);

    public static bool HasProject(string dir) => File.Exists(ProjectPath(dir));

    public static string SafeFileName(string name)
    {
        var cleaned = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(cleaned) ? "Без названия" : cleaned;
    }
}
