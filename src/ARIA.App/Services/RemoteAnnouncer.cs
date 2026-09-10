namespace Aria.App.Services;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Aria.Remote;
using Makaretu.Dns;
using QRCoder;

public sealed record RemoteInfo(Uri Url, byte[] QrPng);

public sealed class RemoteAnnouncer : IDisposable
{
    private readonly Uri _httpEndpoint;
    private readonly Func<RemoteCredentials> _pair;
    private readonly object _gate = new();
    private ServiceDiscovery? _discovery;
    private IPAddress? _lanAddress;

    public RemoteAnnouncer(Uri httpEndpoint, Func<RemoteCredentials> pair)
    {
        _httpEndpoint = httpEndpoint;
        _pair = pair;
    }

    public RemoteAnnouncer(Uri httpEndpoint, RemoteCredentials credentials)
        : this(httpEndpoint, () => credentials)
    {
    }

    public RemoteInfo? Announce()
    {
        var address = FindLanAddress();
        lock (_gate)
        {
            _lanAddress = address;
        }
        var pair = _pair();
        return address is null ? null : BuildInfo(_httpEndpoint, pair.Identifier, pair.Password, address);
    }

    public bool Start()
    {
        lock (_gate)
        {
            if (_discovery is not null)
            {
                return true;
            }
            if (_lanAddress is null)
            {
                return false;
            }
            try
            {
                var discovery = new ServiceDiscovery();
                discovery.Advertise(new ServiceProfile(
                    "aria",
                    "_aria._tcp",
                    (ushort)_httpEndpoint.Port,
                    [_lanAddress]));
                _discovery = discovery;
                _ = Task.Run(() =>
                {
                    try
                    {
                        discovery.Announce(new ServiceProfile(
                            "aria",
                            "_aria._tcp",
                            (ushort)_httpEndpoint.Port,
                            [_lanAddress]));
                    }
                    catch (Exception)
                    {
                    }
                });
                return true;
            }
            catch (Exception)
            {
                _discovery?.Dispose();
                _discovery = null;
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_discovery is not { } discovery)
            {
                return;
            }
            _discovery = null;
            try
            {
                discovery.Unadvertise();
            }
            catch (Exception)
            {
            }
            try
            {
                discovery.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    public static string BuildUrl(Uri endpoint, string identifier, string password, IPAddress lanIp)
    {
        var host = lanIp.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{lanIp}]" : lanIp.ToString();
        return $"http://{host}:{endpoint.Port}/?id={Uri.EscapeDataString(identifier)}&key={Uri.EscapeDataString(password)}";
    }

    public static RemoteInfo BuildInfo(Uri endpoint, string identifier, string password, IPAddress lanIp)
    {
        var url = BuildUrl(endpoint, identifier, password, lanIp);
        var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(16);
        return new RemoteInfo(new Uri(url), png);
    }

    private static IPAddress? FindLanAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }
                var ip = unicast.Address;
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    continue;
                }
                return ip;
            }
        }
        return null;
    }
}