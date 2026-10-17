using System.Text.Json;

namespace Bladez.Core;

public sealed class HostSettings
{
    public string ListenUrl { get; set; } = "http://127.0.0.1:5088";
    public string PublicUrl { get; set; } = "http://127.0.0.1:5088";
    public string? TrustedProxy { get; set; }
    public bool AllowLoopbackHttp { get; set; } = true;
    public int DatabasePort { get; set; } = 33389;
    public string DatabaseName { get; set; } = "bladez_business";
    public string DatabaseUser { get; set; } = "bladez_app";
    public bool StartMinimized { get; set; }

    public static string DataDirectory => Path.GetFullPath(Environment.GetEnvironmentVariable("BLADEZ_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bladez", "Host"));

    public static HostSettings Load(string directory)
    {
        var path = Path.Combine(directory, "server.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(path)) ?? new() : new();
    }
    public void Save(string directory) => PrivateFiles.Write(Path.Combine(directory, "server.json"), JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
}
