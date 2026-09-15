namespace Aria.App.Tests;

using Aria.App.Views.SettingsSections;

public sealed class SettingsSectionsTests
{
    [Fact]
    public void Registry_HasSevenSections_InOrder()
    {
        var titles = SettingsSectionRegistry.All.Select(s => s.Title).ToArray();

        Assert.Equal(["Пульт", "Жесты", "Часы", "Формат строк", "Воспроизведение", "Звук", "Движок"], titles);
    }

    [Fact]
    public void Registry_KeysAreUnique_AndFactoriesCreateControls()
    {
        var keys = SettingsSectionRegistry.All.Select(s => s.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
        foreach (var section in SettingsSectionRegistry.All)
        {
            Assert.NotNull(section.Create);
        }
    }
}
