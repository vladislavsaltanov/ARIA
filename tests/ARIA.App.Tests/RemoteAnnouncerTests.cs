namespace Aria.App.Tests;

using System.Net;
using Aria.App.Services;
using Aria.Remote;

public sealed class RemoteAnnouncerTests
{
    private static readonly RemoteCredentials Pair = new("ARIA-STAGE", "pass-123");

    [Fact]
    public void BuildUrl_ReplacesHostWithLanAddress_KeepsPortAndPair()
    {
        var url = RemoteAnnouncer.BuildUrl(new Uri("http://127.0.0.1:5432/"), Pair.Identifier, Pair.Password, IPAddress.Parse("192.168.1.5"));

        Assert.Equal("http://192.168.1.5:5432/?id=ARIA-STAGE&key=pass-123", url);
    }

    [Fact]
    public void BuildUrl_EncodesPair()
    {
        var url = RemoteAnnouncer.BuildUrl(new Uri("http://127.0.0.1:5432/"), "ARIA A/B", "p w+d", IPAddress.Parse("10.0.0.2"));

        Assert.Equal("http://10.0.0.2:5432/?id=ARIA%20A%2FB&key=p%20w%2Bd", url);
    }

    [Fact]
    public void BuildInfo_EncodesUrlAndPngQr()
    {
        var info = RemoteAnnouncer.BuildInfo(new Uri("http://127.0.0.1:5432/"), "ARIA-X1", "p4ss", IPAddress.Parse("192.168.1.5"));

        Assert.Equal(new Uri("http://192.168.1.5:5432/?id=ARIA-X1&key=p4ss"), info.Url);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, info.QrPng[..8]);
        Assert.True(info.QrPng.Length > 100);
    }

    [Fact]
    public void Announce_UsesCurrentPairFromProvider()
    {
        using var announcer = new RemoteAnnouncer(new Uri("http://127.0.0.1:5432/"), () => Pair);
        var info = announcer.Announce();

        Assert.NotNull(info);
        Assert.Contains("id=ARIA-STAGE", info!.Url.Query);
        Assert.Contains("key=pass-123", info.Url.Query);
    }

    [Fact]
    public void Start_BeforeAnnounce_ReturnsFalseWithoutNetworkSetup()
    {
        using var announcer = new RemoteAnnouncer(new Uri("http://127.0.0.1:5432/"), Pair);

        Assert.False(announcer.Start());
    }
}
