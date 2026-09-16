namespace Aria.Audio.Tests;

public sealed class OutputDeviceTests
{
    [Fact]
    public void ListPlaybackDevices_ContainsExactlyOneDefault()
    {
        var devices = new MiniaudioOutputLister().ListPlaybackDevices();

        Assert.NotEmpty(devices);
        Assert.Single(devices, d => d.IsDefault);
        Assert.All(devices, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
        Assert.Equal(devices.Count, devices.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());
    }
}
