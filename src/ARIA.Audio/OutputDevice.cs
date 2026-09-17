namespace Aria.Audio;

using System.Text;
using Aria.Audio.Native;

public sealed record OutputDevice(string Id, string Name, bool IsDefault);

public interface IAudioOutputLister
{
    IReadOnlyList<OutputDevice> ListPlaybackDevices();
}

public sealed class MiniaudioOutputLister : IAudioOutputLister
{
    private const int NameCapacity = 512;

    public IReadOnlyList<OutputDevice> ListPlaybackDevices()
    {
        var count = AriaShim.OutputCount();
        if (count <= 0)
        {
            return [];
        }
        var idSize = AriaShim.OutputDeviceIdSize();
        if (idSize <= 0)
        {
            return [];
        }
        var devices = new List<OutputDevice>(count);
        var nameBuffer = new byte[NameCapacity];
        var idBuffer = new byte[idSize];
        for (var index = 0; index < count; index++)
        {
            if (AriaShim.OutputInfo(index, nameBuffer, nameBuffer.Length, idBuffer, idBuffer.Length, out var isDefault) != 0)
            {
                continue;
            }
            var nul = Array.IndexOf(nameBuffer, (byte)0);
            var name = Encoding.UTF8.GetString(nameBuffer, 0, nul >= 0 ? nul : nameBuffer.Length);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            devices.Add(new OutputDevice(Convert.ToBase64String(idBuffer), name, isDefault != 0));
        }
        return devices;
    }
}
