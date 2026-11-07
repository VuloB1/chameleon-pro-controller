using System.Net.Http;
using System.Text.Json;

// Pregunta a GitHub cual es la ultima release. No descarga ni instala nada: solo avisa.
static class Update
{
    const string Api = "https://api.github.com/repos/VuloB1/chameleon-pro-controller/releases/latest";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10), DefaultRequestHeaders = { { "User-Agent", "Chameleon" } } };

    public static Version Current => typeof(App).Assembly.GetName().Version!;

    // Devuelve (version, url de la release) si hay una mas nueva; null si no, o si falla la red.
    public static async Task<(string ver, string url)?> Check()
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
            var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
            var url = doc.RootElement.GetProperty("html_url").GetString()!;
            return Newer(tag, Current) ? (tag.TrimStart('v'), url) : null;
        }
        catch { return null; } // sin internet o limite de la API: se reintenta en 24 h
    }

    public static bool Newer(string tag, Version cur) =>
        Version.TryParse(tag.TrimStart('v'), out var v) && v > new Version(cur.Major, cur.Minor, Math.Max(cur.Build, 0));
}
