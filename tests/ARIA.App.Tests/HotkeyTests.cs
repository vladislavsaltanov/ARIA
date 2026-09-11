namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.Views;
using Avalonia.Input;

public sealed class HotkeyTests
{
    [Fact]
    public void TryHandle_MatchesCaseInsensitive_AndCanonicalizes()
    {
        var actions = new List<string>();
        var service = new HotkeyService(HotkeyConfig.Default, actions.Add);

        Assert.True(service.TryHandle("SHIFT+CTRL+P"));

        Assert.Equal("panic", Assert.Single(actions));
    }

    [Fact]
    public void Space_MapsToPlayAction()
    {
        var actions = new List<string>();
        var service = new HotkeyService(HotkeyConfig.Default, actions.Add);

        Assert.True(service.TryHandle("space"));

        Assert.Equal("play", Assert.Single(actions));
    }

    [Fact]
    public void UnknownGesture_ReturnsFalse()
    {
        var actions = new List<string>();
        var service = new HotkeyService(HotkeyConfig.Default, actions.Add);

        Assert.False(service.TryHandle("f13"));

        Assert.Empty(actions);
    }

    [Fact]
    public void FirstMatchWins()
    {
        var actions = new List<string>();
        var config = new HotkeyConfig([new HotkeyBinding("Space", "play"), new HotkeyBinding("space", "pause")]);
        var service = new HotkeyService(config, actions.Add);

        Assert.True(service.TryHandle("SPACE"));

        Assert.Equal("play", Assert.Single(actions));
    }

    [Fact]
    public void Config_Roundtrip()
    {
        var path = TempPath();
        try
        {
            var config = new HotkeyConfig(
            [
                new HotkeyBinding("Ctrl+Shift+P", "panic"),
                new HotkeyBinding("Space", "play"),
            ]);

            config.Save(path);
            var loaded = HotkeyConfig.Load(path);

            Assert.Equal(2, loaded.Bindings.Length);
            Assert.Equal(config.Bindings[0], loaded.Bindings[0]);
            Assert.Equal(config.Bindings[1], loaded.Bindings[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Config_CorruptFile_ReturnsDefault()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ bindings: [oops");

            var loaded = HotkeyConfig.Load(path);

            Assert.Equal(HotkeyConfig.Default.Bindings.Length, loaded.Bindings.Length);
            Assert.Equal(HotkeyConfig.Default.Bindings[0], loaded.Bindings[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Config_MissingFile_ReturnsDefault()
    {
        var path = TempPath();

        var loaded = HotkeyConfig.Load(path);

        Assert.Equal(HotkeyConfig.Default.Bindings.Length, loaded.Bindings.Length);
        Assert.Equal(HotkeyConfig.Default.Bindings[2], loaded.Bindings[2]);
    }

    [Fact]
    public void Default_ContainsEightActions()
    {
        var service = new HotkeyService(HotkeyConfig.Default, _ => { });

        Assert.Equal("Ctrl+T", service.GestureFor("toggle-script"));
        Assert.Equal("Ctrl+Shift+C", service.GestureFor("reset-clock"));
        Assert.Equal("Space", service.GestureFor("play"));
        Assert.Equal("Esc", service.GestureFor("pause"));
        Assert.Equal("Ctrl+Shift+P", service.GestureFor("panic"));
    }

    [Fact]
    public void GestureFor_UnknownAction_ReturnsEmpty()
    {
        var service = new HotkeyService(HotkeyConfig.Default, _ => { });

        Assert.Equal(string.Empty, service.GestureFor("no-such-action"));
    }

    [Theory]
    [InlineData(Key.Space, KeyModifiers.None, "space")]
    [InlineData(Key.Escape, KeyModifiers.None, "escape")]
    [InlineData(Key.T, KeyModifiers.Control, "ctrl+t")]
    [InlineData(Key.P, KeyModifiers.Control | KeyModifiers.Shift, "ctrl+shift+p")]
    [InlineData(Key.D5, KeyModifiers.None, "5")]
    public void HotkeyInput_BuildsCanonicalGesture(Key key, KeyModifiers modifiers, string gesture)
    {
        Assert.Equal(gesture, HotkeyInput.GestureFor(key, modifiers));
    }

    [Fact]
    public void Reset_ReplacesBindings()
    {
        var service = new HotkeyService(HotkeyConfig.Default, _ => { });
        service.Reset(new HotkeyConfig([new HotkeyBinding("F5", "play")]));

        Assert.Equal("f5", service.GestureFor("play"));
        Assert.Equal(string.Empty, service.GestureFor("pause"));
    }

    [Fact]
    public void ConflictFor_ReportsOtherAction()
    {
        var service = new HotkeyService(HotkeyConfig.Default, _ => { });

        Assert.Equal("play", service.ConflictFor("pause", "Space"));
        Assert.Null(service.ConflictFor("play", "Space"));
        Assert.Null(service.ConflictFor("pause", "F9"));
    }

    [Theory]
    [InlineData("Meta+Q", true)]
    [InlineData("meta+tab", true)]
    [InlineData("Ctrl+Space", true)]
    [InlineData("Ctrl+T", false)]
    [InlineData("Space", false)]
    public void IsSystemReserved_FlagsMacOsGestures(string gesture, bool reserved)
    {
        Assert.Equal(reserved, HotkeyService.IsSystemReserved(gesture));
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        "aria-hotkeys-" + Guid.NewGuid().ToString("N") + ".json");
}
