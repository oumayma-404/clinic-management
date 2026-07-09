using System;
using System.IO;
using System.Text.Json;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// The clinic server address this shell connects to. Persisted per-user so a staff PC is configured
/// once and reused on every launch (AC-2.2). The shell always connects over HTTPS to the Kestrel front
/// door — the single browser-facing endpoint (plan Decision #2) — never the internal Next web port.
/// </summary>
public sealed class ServerConfig
{
    /// <summary>Server hostname or LAN IPv4 (e.g. "clinic-server" or "192.168.1.10"). "localhost" on the server PC (AC-2.5).</summary>
    public string Host { get; set; } = "";

    /// <summary>The Kestrel HTTPS front-door port (`Hosting:HttpsPort`, default 5001).</summary>
    public int Port { get; set; } = DefaultHttpsPort;

    /// <summary>Default HTTPS front-door port — matches the API's `Hosting:HttpsPort` default.</summary>
    public const int DefaultHttpsPort = 5001;

    /// <summary>The absolute HTTPS URL the WebView2 control navigates to.</summary>
    public string BaseUrl => $"https://{Host}:{Port}";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>
/// Reads/writes <see cref="ServerConfig"/> from <c>%AppData%\ClinicManagement\server.json</c> (AC-2.2).
/// A missing or unreadable file is treated as "not configured" so first launch shows the address prompt.
/// </summary>
public static class ServerConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClinicManagement");

    public static string FilePath => Path.Combine(Directory, "server.json");

    public static ServerConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new ServerConfig();
            }

            var json = File.ReadAllText(FilePath);
            var config = JsonSerializer.Deserialize<ServerConfig>(json);
            return config ?? new ServerConfig();
        }
        catch
        {
            // Corrupt/unreadable config → fall back to the first-run prompt rather than crashing.
            return new ServerConfig();
        }
    }

    public static void Save(ServerConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOptions));
    }

    /// <summary>
    /// Parses a user-entered address into a <see cref="ServerConfig"/>. Accepts a bare host
    /// ("192.168.1.10"), host:port ("192.168.1.10:5001"), or a full URL ("https://clinic-server:5001").
    /// A missing port defaults to <see cref="ServerConfig.DefaultHttpsPort"/>.
    /// </summary>
    public static ServerConfig ParseAddress(string input)
    {
        var value = (input ?? string.Empty).Trim();

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = value["https://".Length..];
        }
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            value = value["http://".Length..];
        }

        var slash = value.IndexOf('/');
        if (slash >= 0)
        {
            value = value[..slash];
        }

        var host = value;
        var port = ServerConfig.DefaultHttpsPort;

        var colon = value.LastIndexOf(':');
        if (colon >= 0)
        {
            host = value[..colon];
            if (int.TryParse(value[(colon + 1)..], out var parsed) && parsed is > 0 and <= 65535)
            {
                port = parsed;
            }
        }

        return new ServerConfig { Host = host.Trim(), Port = port };
    }
}
