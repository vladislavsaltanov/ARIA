namespace Aria.App.ViewModels;

using System.Windows.Input;
using Aria.App.Services;
using Aria.Remote;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class RemotePanelViewModel : ObservableObject
{
    private readonly SynchronizationContext? _sync;
    private Func<(RemoteInfo? Info, Bitmap? Qr, RemoteCredentials Credentials)>? _resetPair;

    [ObservableProperty]
    private string urlText = "—";

    [ObservableProperty]
    private Bitmap? qrImage;

    [ObservableProperty]
    private string mdnsStatus = "анонс в локальной сети: —";

    [ObservableProperty]
    private string identifierText = "";

    [ObservableProperty]
    private string passwordText = "";

    [ObservableProperty]
    private bool hasInfo;

    public ICommand ResetPasswordCommand { get; }

    public RemotePanelViewModel(SynchronizationContext? sync = null)
    {
        _sync = sync;
        ResetPasswordCommand = new RelayCommand(ResetPassword);
    }

    public void Configure(RemoteInfo? info, Bitmap? qr, RemoteCredentials credentials, Func<(RemoteInfo? Info, Bitmap? Qr, RemoteCredentials Credentials)> resetPair) => Post(() =>
    {
        _resetPair = resetPair;
        IdentifierText = credentials.Identifier;
        PasswordText = credentials.Password;
        ApplyInfo(info, qr);
    });

    public void Init(RemoteInfo? info, Bitmap? qr) => Post(() => ApplyInfo(info, qr));

    public void ResetPassword() => Post(() =>
    {
        if (_resetPair is not { } reset)
        {
            return;
        }
        var (info, qr, credentials) = reset();
        IdentifierText = credentials.Identifier;
        PasswordText = credentials.Password;
        ApplyInfo(info, qr);
    });

    public void SetMdns(bool enabled) => Post(() =>
        MdnsStatus = enabled ? "анонс в локальной сети: вкл" : "анонс в локальной сети: выкл");

    private void ApplyInfo(RemoteInfo? info, Bitmap? qr)
    {
        if (info is { } value)
        {
            UrlText = value.Url.ToString();
            QrImage = qr;
            HasInfo = true;
        }
        else
        {
            UrlText = "LAN-адрес не найден";
            QrImage = null;
            HasInfo = false;
        }
    }

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
