namespace Aria.App.Services;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Makaretu.Dns;
using QRCoder;

public sealed record RemoteInfo(Uri Url, byte[] QrPng);

public sealed class RemoteAnnouncer : IDisposable
{
    private readonly Uri _httpEndpoint;
    private readonly string _token;
    private readonly object _gate = new();
    private ServiceDiscovery? _discovery;
    private IPAddress? _lanAddress;

    public RemoteAnnouncer(Uri httpEndpoint, string token)
    {
        _httpEndpoint = httpEndpoint;
        _token = token;
    }

    public RemoteInfo? Announce()
    {
        var address = FindLanAddress();
        lock (_gate)
        {
            _lanAddress = address;
        }
        return address is null ? null : BuildInfo(_httpEndpoint, _token, address);
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

    public static string BuildUrl(Uri endpoint, string token, IPAddress lanIp)
    {
        var host = lanIp.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{lanIp}]" : lanIp.ToString();
        return $"http://{host}:{endpoint.Port}/?token={Uri.EscapeDataString(token)}";
    }

    public static RemoteInfo BuildInfo(Uri endpoint, string token, IPAddress lanIp)
    {
        var url = BuildUrl(endpoint, token, lanIp);
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