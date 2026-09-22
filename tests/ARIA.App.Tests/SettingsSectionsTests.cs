namespace Aria.App.Tests;

using Aria.App.Views.SettingsSections;

public sealed class SettingsSectionsTests
{
    [Fact]
    public void Registry_HasNineSections_InOrder()
    {
        var titles = SettingsSectionRegistry.All.Select(s => s.Title).ToArray();

        Assert.Equal(["Пульт", "Мониторы", "Жесты", "Часы", "Формат строк", "Воспроизведение", "Звук", "Движок", "Журнал"], titles);
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
