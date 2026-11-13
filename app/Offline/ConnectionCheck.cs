using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WinSqueeze.Server;
using WinSqueeze.Settings;
namespace WinSqueeze.Offline;

// Why the server can't be reached, in three steps: is this PC on a network, does the server's PC answer a ping, does Lyrion answer on its port (SPEC.md › Offline downloads)
public record ConnectionReport(bool Network, string NetworkKind, bool Ping, long PingMs, bool Lyrion, string LyrionProblem, int HttpStatus)
{
    public string Summary => $"network {(Network ? "up " + NetworkKind : "down")}, ping {(Ping ? PingMs + " ms" : "no reply")}, lyrion {(Lyrion ? "answers" : LyrionProblem + (HttpStatus > 0 ? " " + HttpStatus : ""))}";
}

public static class ConnectionCheck
{
    public const int PingTimeoutMs = 2000, PortTimeoutMs = 4000, ReplyTimeoutSeconds = 10;
    static readonly object logLock = new();

    public static async Task<ConnectionReport> RunAsync(ServerProfile profile)
    {
        var (networkUp, networkKind) = ActiveNetwork();
        var pingTask = PingAsync(profile.Host); var lyrionTask = LyrionAsync(profile);
        var (pingReplied, pingMs) = await pingTask; var (lyrionProblem, httpStatus) = await lyrionTask;
        return new ConnectionReport(networkUp, networkKind, pingReplied, pingMs, lyrionProblem == "", lyrionProblem, httpStatus);
    }

    // The connected adapter that leads somewhere (has a gateway); a VPN's own adapter counts, since it is then the route
    static (bool up, string kind) ActiveNetwork()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable()) return (false, "");
            var connected = NetworkInterface.GetAllNetworkInterfaces().Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)).ToList();
            var routed = connected.FirstOrDefault(adapter => adapter.GetIPProperties().GatewayAddresses.Any(gateway => !gateway.Address.Equals(System.Net.IPAddress.Any))) ?? connected.FirstOrDefault();
            if (routed == null) return (false, "");
            return (true, routed.NetworkInterfaceType switch { NetworkInterfaceType.Wireless80211 => "Wi-Fi", NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Ethernet", _ => routed.Name });
        }
        catch { return (true, ""); }
    }

    static async Task<(bool replied, long milliseconds)> PingAsync(string host)
    {
        try { using var ping = new Ping(); var reply = await ping.SendPingAsync(host, PingTimeoutMs); return (reply.Status == IPStatus.Success, reply.RoundtripTime); }
        catch { return (false, 0); }
    }

    // "" when Lyrion answers; else notfound (name doesn't resolve), refused (nothing listening), timeout, password, http (see status) or error
    static async Task<(string problem, int httpStatus)> LyrionAsync(ServerProfile profile)
    {
        try
        {
            using var portProbe = new TcpClient();
            using var portTimeout = new CancellationTokenSource(PortTimeoutMs);
            await portProbe.ConnectAsync(profile.Host, profile.Port, portTimeout.Token);
        }
        catch (OperationCanceledException) { return ("timeout", 0); }
        catch (SocketException socketError) { return (socketError.SocketErrorCode switch { SocketError.ConnectionRefused => "refused", SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "notfound", SocketError.TimedOut => "timeout", _ => "unreachable" }, 0); }
        catch { return ("error", 0); }
        try { using var lyrionClient = new LyrionClient(profile); await lyrionClient.GetServerStatusAsync(); return ("", 0); }
        catch (TaskCanceledException) { return ("noreply", 0); }
        catch (HttpRequestException httpError) when (httpError.StatusCode is { } statusCode) { return ((int)statusCode == 401 ? "password" : "http", (int)statusCode); }
        catch { return ("error", 0); }
    }

    // logs\connection.log: when and why the app went offline and came back; no addresses. Kept to its last 300 lines
    public static void Log(string line)
    {
        try
        {
            lock (logLock)
            {
                var logPath = Path.Combine(AppPaths.LogsFolder, "connection.log");
                var keptLines = File.Exists(logPath) ? File.ReadAllLines(logPath).TakeLast(299).ToList() : new List<string>();
                keptLines.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}");
                File.WriteAllLines(logPath, keptLines);
            }
        }
        catch { }
    }
}
