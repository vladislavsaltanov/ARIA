namespace Aria.App.ViewModels;

using Aria.App.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class RemotePanelViewModel : ObservableObject
{
    private readonly SynchronizationContext? _sync;

    [ObservableProperty]
    private string urlText = "—";

    [ObservableProperty]
    private Bitmap? qrImage;

    [ObservableProperty]
    private string mdnsStatus = "анонс в локальной сети: —";

    [ObservableProperty]
    private bool hasInfo;

    public RemotePanelViewModel(SynchronizationContext? sync = null)
    {
        _sync = sync;
    }

    public void Init(RemoteInfo? info) => Post(() =>
    {
        if (info is { } value)
        {
            UrlText = value.Url.ToString();
            QrImage = new Bitmap(new MemoryStream(value.QrPng));
            HasInfo = true;
        }
        else
        {
            UrlText = "LAN-адрес не найден";
            QrImage = null;
            HasInfo = false;
        }
    });

    public void SetMdns(bool enabled) => Post(() =>
        MdnsStatus = enabled ? "анонс в локальной сети: вкл" : "анонс в локальной сети: выкл");

    private void Post(Action work)
    {
        if (_sync is { } sync)
        {
            sync.Post(_ => work(), null);
        }
        else
        {
            work();
        }
    }
}