namespace Aria.App.Tests;

using Aria.App.Services;
using Aria.App.ViewModels;
using Aria.Remote;

public sealed class RemotePanelViewModelTests
{
    [Fact]
    public void Configure_ShowsIdentifierAndPassword_WithoutLanAddress()
    {
        var viewModel = new RemotePanelViewModel();

        viewModel.Configure(null, null, new RemoteCredentials("ARIA-STAGE", "pw"), () => (null, null, new RemoteCredentials("ARIA-STAGE", "pw")));

        Assert.Equal("ARIA-STAGE", viewModel.IdentifierText);
        Assert.Equal("pw", viewModel.PasswordText);
        Assert.False(viewModel.HasInfo);
    }

    [Fact]
    public void Init_WithInfo_ShowsUrl()
    {
        var viewModel = new RemotePanelViewModel();
        var info = new RemoteInfo(new Uri("http://192.168.1.5:5432/?id=ARIA-STAGE&key=pw"), []);

        viewModel.Configure(info, null, new RemoteCredentials("ARIA-STAGE", "pw"), () => (info, null, new RemoteCredentials("ARIA-STAGE", "pw")));

        Assert.True(viewModel.HasInfo);
        Assert.Equal(info.Url.ToString(), viewModel.UrlText);
    }

    [Fact]
    public void ResetCommand_RefreshesPasswordAndUrl()
    {
        var viewModel = new RemotePanelViewModel();
        var resetCount = 0;
        var fresh = new RemoteCredentials("ARIA-STAGE", "new-pw");
        var freshInfo = new RemoteInfo(new Uri("http://192.168.1.5:5432/?id=ARIA-STAGE&key=new-pw"), []);

        var initialInfo = new RemoteInfo(new Uri("http://192.168.1.5:5432/?id=ARIA-STAGE&key=pw"), []);
        viewModel.Configure(initialInfo, null, new RemoteCredentials("ARIA-STAGE", "pw"), () =>
        {
            resetCount++;
            return (freshInfo, null, fresh);
        });

        viewModel.ResetPasswordCommand.Execute(null);

        Assert.Equal(1, resetCount);
        Assert.Equal("new-pw", viewModel.PasswordText);
        Assert.Equal("ARIA-STAGE", viewModel.IdentifierText);
        Assert.Equal(freshInfo.Url.ToString(), viewModel.UrlText);
    }

    [Fact]
    public void Init_WithoutLanAddress_ShowsNoInfo()
    {
        var viewModel = new RemotePanelViewModel();

        viewModel.Configure(null, null, new RemoteCredentials("ARIA-STAGE", "pw"), () => (null, null, new RemoteCredentials("ARIA-STAGE", "pw")));

        Assert.False(viewModel.HasInfo);
        Assert.Equal("LAN-адрес не найден", viewModel.UrlText);
    }

    [Fact]
    public void ResetCommand_WithoutConfigure_DoesNothing()
    {
        var viewModel = new RemotePanelViewModel();

        viewModel.ResetPasswordCommand.Execute(null);

        Assert.False(viewModel.HasInfo);
    }

    [Fact]
    public void ConfigureListen_ShowsListenPair()
    {
        var viewModel = new RemotePanelViewModel();
        var info = new RemoteInfo(new Uri("http://192.168.1.5:5432/monitor?id=ARIA-STAGE&key=lp"), []);

        viewModel.ConfigureListen(info, null, "lp", () => (info, null, "lp"));

        Assert.True(viewModel.HasListenInfo);
        Assert.Equal("lp", viewModel.ListenPasswordText);
        Assert.Equal(info.Url.ToString(), viewModel.ListenUrlText);
    }

    [Fact]
    public void ResetListenPassword_UpdatesTextAndUrl()
    {
        var viewModel = new RemotePanelViewModel();
        var info = new RemoteInfo(new Uri("http://192.168.1.5:5432/monitor?id=ARIA-STAGE&key=lp"), []);
        var fresh = new RemoteInfo(new Uri("http://192.168.1.5:5432/monitor?id=ARIA-STAGE&key=new"), []);
        var resets = 0;
        viewModel.ConfigureListen(info, null, "lp", () =>
        {
            resets++;
            return (fresh, null, "new");
        });

        viewModel.ResetListenPasswordCommand.Execute(null);

        Assert.Equal(1, resets);
        Assert.Equal("new", viewModel.ListenPasswordText);
        Assert.Equal(fresh.Url.ToString(), viewModel.ListenUrlText);
    }
}
