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
    private Func<(RemoteInfo? Info, Bitmap? Qr, string Password)>? _resetListen;

    [ObservableProperty]
    private string listenUrlText = "—";

    [ObservableProperty]
    private Bitmap? listenQrImage;

    [ObservableProperty]
    private string listenPasswordText = "";

    [ObservableProperty]
    private bool hasListenInfo;

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

    public ICommand ResetListenPasswordCommand { get; }

    public RemotePanelViewModel(SynchronizationContext? sync = null)
    {
        _sync = sync;
        ResetPasswordCommand = new RelayCommand(ResetPassword);
        ResetListenPasswordCommand = new RelayCommand(ResetListenPassword);
    }

    public void Configure(RemoteInfo? info, Bitmap? qr, RemoteCredentials credentials, Func<(RemoteInfo? Info, Bitmap? Qr, RemoteCredentials Credentials)> resetPair) => Post(() =>
    {
        _resetPair = resetPair;
        IdentifierText = credentials.Identifier;
        PasswordText = credentials.Password;
        ApplyInfo(info, qr);
    });

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

    public void ConfigureListen(RemoteInfo? info, Bitmap? qr, string password, Func<(RemoteInfo? Info, Bitmap? Qr, string Password)> reset) => Post(() =>
    {
        _resetListen = reset;
        ApplyListen(info, qr, password);
    });

    public void ResetListenPassword() => Post(() =>
    {
        if (_resetListen is not { } reset)
        {
            return;
        }
        var (info, qr, password) = reset();
        ApplyListen(info, qr, password);
    });

    private void ApplyListen(RemoteInfo? info, Bitmap? qr, string password)
    {
        ListenPasswordText = password;
        ListenQrImage = qr;
        HasListenInfo = info is not null;
        ListenUrlText = info?.Url.ToString() ?? "LAN-адрес не найден";
    }

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
