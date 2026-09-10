namespace Aria.App.Tests;

using System.Net;
using Aria.App.Services;

public sealed class RemoteAnnouncerTests
{
    [Fact]
    public void BuildUrl_ReplacesHostWithLanAddress_KeepsPortAndToken()
    {
        var url = RemoteAnnouncer.BuildUrl(new Uri("http://127.0.0.1:5432/"), "secret", IPAddress.Parse("192.168.1.5"));

        Assert.Equal("http://192.168.1.5:5432/?token=secret", url);
    }

    [Fact]
    public void BuildUrl_EncodesToken()
    {
        var url = RemoteAnnouncer.BuildUrl(new Uri("http://127.0.0.1:5432/"), "a b/c+d", IPAddress.Parse("10.0.0.2"));

        Assert.Equal("http://10.0.0.2:5432/?token=a%20b%2Fc%2Bd", url);
    }

    [Fact]
    public void BuildInfo_EncodesUrlAndPngQr()
    {
        var info = RemoteAnnouncer.BuildInfo(new Uri("http://127.0.0.1:5432/"), "t0ken", IPAddress.Parse("192.168.1.5"));

        Assert.Equal(new Uri("http://192.168.1.5:5432/?token=t0ken"), info.Url);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, info.QrPng[..8]);
        Assert.True(info.QrPng.Length > 100);
    }

    [Fact]
    public void Start_BeforeAnnounce_ReturnsFalseWithoutNetworkSetup()
    {
        using var announcer = new RemoteAnnouncer(new Uri("http://127.0.0.1:5432/"), "t");

        Assert.False(announcer.Start());
    }
}