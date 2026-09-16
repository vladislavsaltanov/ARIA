namespace Aria.App.Services;

using System.Text;

internal static class UnicodePaths
{
    internal static string Key(string path) => path.Normalize(NormalizationForm.FormC);
}
