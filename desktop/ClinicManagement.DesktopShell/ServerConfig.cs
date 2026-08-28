using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Whether <see cref="Port"/> came from the user (or from a resolution that already happened) rather than
    /// from a default. False means <see cref="CandidatePorts"/> is probed before connecting — see the type note.
    /// </summary>
    public bool PortIsExplicit { get; set; }

    /// <summary>Default HTTPS front-door port of a clinic's own PC — matches the API's `Hosting:HttpsPort` default.</summary>
    public const int DefaultHttpsPort = 5001;

    /// <summary>The port a hosted deployment is reached on over the internet, behind Caddy.</summary>
    public const int DefaultPublicHttpsPort = 443;

    /// <summary>
    /// The one `HostedMultiTenant` deployment — the backend every clinic on the hosted plan shares. Chosen by
    /// the « APEXA Cloud » button on first run so that clinic is never asked for an address it has no way of
    /// knowing.
    ///
    /// ⚠️ THIS IS THE ONE SERVER-SPECIFIC STRING IN THE SHELL, and it is the thing that breaks on the day the
    /// deployment moves. Installed clients do not re-read it, so a domain change strands every one of them on a
    /// dead host. Two things keep that recoverable and must stay true: « Changer de serveur » returns to the
    /// mode chooser, and the LAN branch's field accepts ANY address — so a stranded user can type the new
    /// domain instead of reinstalling. Do not make the hosted branch the only way to reach a hosted server.
    ///
    /// ⚠️ It is an OVH default hostname, not a product domain. When APEXA gets its own (app.apexa.tn or
    /// similar), change it HERE and ship a new client; nothing else in the shell names a host.
    /// </summary>
    public const string HostedHost = "vps-dc7e4229.vps.ovh.net";

    /// <summary>
    /// The hosted deployment, ready to connect. The port is marked explicit because 443 is not a guess here:
    /// it is what the deployment runs on, so the probe that exists for a typed address would be a round trip
    /// spent confirming something already known.
    /// </summary>
    public static ServerConfig Hosted() =>
        new() { Host = HostedHost, Port = DefaultPublicHttpsPort, PortIsExplicit = true };

    /// <summary>
    /// The ports to try, in order, when connecting. One entry when the user typed a port — it is used verbatim
    /// and never probed. Otherwise <see cref="DefaultPublicHttpsPort"/> **before** <see cref="DefaultHttpsPort"/>:
    /// a LAN server refuses 443 instantly, whereas an internet firewall in front of a hosted server usually
    /// *drops* traffic to 5001, so trying the LAN port first would cost a full timeout on every hosted launch.
    /// </summary>
    public IReadOnlyList<int> CandidatePorts => PortIsExplicit
        ? new[] { Port }
        : new[] { DefaultPublicHttpsPort, DefaultHttpsPort };

    /// <summary>The absolute HTTPS URL the WebView2 control navigates to.</summary>
    public string BaseUrl => $"https://{Host}:{Port}";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// What the address field shows for an already-configured server. The port is omitted while it is still
    /// unresolved: offering ":5001" back to someone who typed a hosted domain would invite them to confirm a
    /// port that is wrong, and it is not what they typed.
    /// </summary>
    public string DisplayAddress => PortIsExplicit ? $"{Host}:{Port}" : Host;

    /// <summary>
    /// The same server on a now-known port. Marked explicit so the probe is a one-time cost per address rather
    /// than a delay on every launch.
    /// </summary>
    public ServerConfig WithResolvedPort(int port) =>
        new() { Host = Host, Port = port, PortIsExplicit = true };
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
    /// <para>
    /// A missing port is left **unresolved** (<see cref="ServerConfig.PortIsExplicit"/> false) rather than
    /// defaulting to 5001, and <see cref="ServerProbe"/> settles it against the real server. Defaulting here is
    /// the defect this shape exists to close: it made every hosted deployment — reached on 443 — unreachable
    /// unless the user knew to type ":443", which nobody typing "clinic.example.com" has any reason to do.
    /// <see cref="ServerConfig.Port"/> still carries 5001 meanwhile, so anything reading it before resolution
    /// (the address field's placeholder) behaves exactly as it did.
    /// </para>
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
        var explicitPort = false;

        var colon = value.LastIndexOf(':');
        if (colon >= 0)
        {
            host = value[..colon];
            if (int.TryParse(value[(colon + 1)..], out var parsed) && parsed is > 0 and <= 65535)
            {
                port = parsed;
                explicitPort = true;
            }
        }

        return new ServerConfig { Host = host.Trim(), Port = port, PortIsExplicit = explicitPort };
    }
}
