namespace Aria.App.Services;

using Aria.Remote;
using Avalonia.Media.Imaging;

public static class RemoteQr
{
    public static Bitmap? ToBitmap(RemoteInfo? info) =>
        info is null ? null : new Bitmap(new MemoryStream(info.QrPng));
}
