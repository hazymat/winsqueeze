using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WinSqueeze.Settings;
namespace WinSqueeze.Server;

public record ServerStatus(string Version, int PlayerCount, long LastScan, int TotalSongs, int TotalAlbums, int TotalArtists);

// Lyrion JSON-RPC (slim.request). The shell is the only part that talks to the server.
public sealed class LyrionClient : IDisposable
{
    readonly HttpClient httpClient;
    public LyrionClient(ServerProfile profile)
    {
        httpClient = new HttpClient { BaseAddress = new Uri($"http://{profile.Host}:{profile.Port}/"), Timeout = TimeSpan.FromSeconds(10) };
        if (profile.Username != "") httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profile.Username}:{profile.Password}")));
    }

    public async Task<JsonElement> RequestAsync(string playerId, params object[] command)
    {
        var requestBody = JsonSerializer.Serialize(new { id = 1, method = "slim.request", @params = new object[] { playerId, command } });
        using var response = await httpClient.PostAsync("jsonrpc.js", new StringContent(requestBody, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return responseDocument.RootElement.TryGetProperty("result", out var result) ? result.Clone() : default;
    }

    public async Task<ServerStatus> GetServerStatusAsync()
    {
        var status = await RequestAsync("", "serverstatus", 0, 0);
        return new ServerStatus(ReadString(status, "version"), ReadInt(status, "player count"), ReadLong(status, "lastscan"), ReadInt(status, "info total songs"), ReadInt(status, "info total albums"), ReadInt(status, "info total artists"));
    }

    // Lyrion returns numbers as numbers or strings depending on the field
    public static string ReadString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? (value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()) : "";
    public static long ReadLong(JsonElement element, string name) => long.TryParse(ReadString(element, name).Split('.')[0], out var number) ? number : 0;
    public static int ReadInt(JsonElement element, string name) => (int)ReadLong(element, name);
    public static double ReadDouble(JsonElement element, string name) => double.TryParse(ReadString(element, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0;

    public void Dispose() => httpClient.Dispose();
}
