namespace Aria.App.Tests;

using Aria.App.ViewModels.Settings;
using Aria.Core.Commands;
using Aria.Core.Playback;

public sealed class MonitorsSectionVmTests
{
    private static SessionProfile Profile(int id, string name) =>
        new(new PreviewSessionHandle(id), name, -3.0, false, -6.0);

    [Fact]
    public void Refresh_ListsSessions()
    {
        var submitted = new List<Command>();
        using var section = new MonitorsSectionVm(submitted.Add, () => [Profile(1, "Drums"), Profile(2, "Keys")]);

        Assert.Equal(2, section.Sessions.Count);
        Assert.Equal("Drums", section.Sessions[0].Name);
        Assert.Equal(-3.0, section.Sessions[0].BackingGainDb);
        Assert.Equal(-6.0, section.Sessions[0].ClickGainDb);
        Assert.Empty(submitted);
    }

    [Fact]
    public void BackingGain_Set_SubmitsAndClamps()
    {
        var submitted = new List<Command>();
        using var section = new MonitorsSectionVm(submitted.Add, () => [Profile(1, "Drums")]);

        section.Sessions[0].BackingGainDb = 20;

        Assert.Equal(12.0, section.Sessions[0].BackingGainDb);
        var command = Assert.IsType<SetSessionBackingGain>(Assert.Single(submitted));
        Assert.Equal(new PreviewSessionHandle(1), command.Session);
        Assert.Equal(12.0, command.GainDb);
    }

    [Fact]
    public void ClickGain_Set_SubmitsCommand()
    {
        var submitted = new List<Command>();
        using var section = new MonitorsSectionVm(submitted.Add, () => [Profile(1, "Drums")]);

        section.Sessions[0].ClickGainDb = -12;

        var command = Assert.IsType<SetSessionClickGain>(Assert.Single(submitted));
        Assert.Equal(new PreviewSessionHandle(1), command.Session);
        Assert.Equal(-12.0, command.GainDb);
    }

    [Fact]
    public void Kick_SubmitsCloseSession()
    {
        var submitted = new List<Command>();
        using var section = new MonitorsSectionVm(submitted.Add, () => [Profile(1, "Drums")]);

        section.Sessions[0].KickCommand.Execute(null);

        var command = Assert.IsType<CloseSession>(Assert.Single(submitted));
        Assert.Equal(new PreviewSessionHandle(1), command.Session);
    }

    [Fact]
    public void Refresh_RemovesGoneSessions_AndUpdatesNames()
    {
        var submitted = new List<Command>();
        var source = new List<SessionProfile> { Profile(1, "Drums"), Profile(2, "Keys") };
        using var section = new MonitorsSectionVm(submitted.Add, () => source);
        var drums = section.Sessions[0];

        source.RemoveAt(1);
        source[0] = source[0] with { Name = "Drums2" };
        section.Refresh();

        Assert.Single(section.Sessions);
        Assert.Same(drums, section.Sessions[0]);
        Assert.Equal("Drums2", section.Sessions[0].Name);
    }
}
