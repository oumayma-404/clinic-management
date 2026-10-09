using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>What a paired PC de secours keeps about itself: who it is, where its cloud is, and its two secrets.</summary>
public sealed record RelayCredentials(
    Guid RelayId,
    Guid ClinicId,
    string ClinicName,
    string CloudUrl,
    string Secret,
    string PrivateKeyBase64,
    DateTime PairedAtUtc)
{
    public byte[] PrivateKey => Convert.FromBase64String(PrivateKeyBase64);

    /// <summary>The cloud's API base, always ending in <c>/api/</c>.</summary>
    public Uri ApiBase => new(CloudUrl.TrimEnd('/') + "/api/");
}

/// <summary>
/// The PC's credentials in <c>.local/relay.json</c>, encrypted under this install's own key ring (<c>clinic-pc-copy</c>
/// D8). A copy of the file without the ring opens nothing; a lost ring means pairing again, never a cloud-side reset.
/// </summary>
public sealed class RelayCredentialStore
{
    public const string FileName = "relay.json";
    public const string Purpose = "ClinicManagement.Relay.Credentials.v1";

    private readonly IDataProtector _protector;
    private readonly string _path;

    public RelayCredentialStore(IDataProtectionProvider protection, string? localDir = null)
    {
        _protector = protection.CreateProtector(Purpose);
        _path = Path.Combine(localDir ?? LocalInstallPaths.LocalDir, FileName);
    }

    public string FilePath => _path;

    public bool Exists => File.Exists(_path);

    /// <summary>Written to a temporary file and moved over, so a crash mid-write never leaves half a credential.</summary>
    public void Save(RelayCredentials credentials)
    {
        AtomicFile.Write(_path, _protector.Protect(JsonSerializer.Serialize(credentials)));
    }

    /// <summary>The credentials, or null when the PC is not paired or the file cannot be opened by this ring.</summary>
    public RelayCredentials? TryLoad()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RelayCredentials>(_protector.Unprotect(File.ReadAllText(_path)));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
