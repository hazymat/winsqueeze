using System.Net;
using System.Net.Sockets;
using System.Text;
namespace WinSqueeze.Server;

public record DiscoveredServer(string Name, string Address, int HttpPort, string Version)
{
    public override string ToString() => $"{Name}  ·  {Address}:{HttpPort}  ·  v{Version}";
}

// Lyrion UDP discovery on port 3483: send 'e' + tag requests, servers answer 'E' + TLV (4-char tag, 1-byte length, value)
public static class ServerFinder
{
    const int discoveryPort = 3483;
    static readonly byte[] discoveryRequest = Encoding.ASCII.GetBytes("eNAME\0JSON\0VERS\0");

    public static async Task<List<DiscoveredServer>> FindAsync(TimeSpan listenTime)
    {
        var servers = new List<DiscoveredServer>();
        using var udpClient = new UdpClient(0) { EnableBroadcast = true };
        await udpClient.SendAsync(discoveryRequest, discoveryRequest.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
        using var listenTimeout = new CancellationTokenSource(listenTime);
        try
        {
            while (true)
            {
                var reply = await udpClient.ReceiveAsync(listenTimeout.Token);
                if (reply.Buffer.Length < 1 || reply.Buffer[0] != (byte)'E') continue;
                var replyTags = ParseTags(reply.Buffer);
                var address = reply.RemoteEndPoint.Address.ToString();
                if (servers.Any(server => server.Address == address)) continue;
                servers.Add(new DiscoveredServer(replyTags.GetValueOrDefault("NAME", address), address, int.TryParse(replyTags.GetValueOrDefault("JSON"), out var httpPort) ? httpPort : 9000, replyTags.GetValueOrDefault("VERS", "?")));
            }
        }
        catch (OperationCanceledException) { }
        return servers;
    }

    static Dictionary<string, string> ParseTags(byte[] replyBytes)
    {
        var replyTags = new Dictionary<string, string>();
        var position = 1;
        while (position + 5 <= replyBytes.Length)
        {
            var tagName = Encoding.ASCII.GetString(replyBytes, position, 4);
            var valueLength = replyBytes[position + 4];
            if (position + 5 + valueLength > replyBytes.Length) break;
            replyTags[tagName] = Encoding.UTF8.GetString(replyBytes, position + 5, valueLength);
            position += 5 + valueLength;
        }
        return replyTags;
    }
}
