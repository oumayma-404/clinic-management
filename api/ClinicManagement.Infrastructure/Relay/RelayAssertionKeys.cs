using System.Security.Cryptography;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// The cloud's prepared-session keys (<c>clinic-pc-copy</c> D22): 32 random bytes per PC de secours, kept on the relay
/// row under the cloud's key ring, and sealed for the PC with its RSA public key — the channel TOTP secrets travel by.
/// </summary>
public sealed class RelayAssertionKeys : IRelayAssertionKeys
{
    public const string Purpose = "ClinicManagement.Relay.AssertionKey.v1";

    private readonly IDataProtector _protector;

    public RelayAssertionKeys(IDataProtectionProvider protection)
    {
        _protector = protection.CreateProtector(Purpose);
    }

    public string NewProtectedKey() => _protector.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public byte[]? Open(string? protectedKey) => Plain(protectedKey) is { } plain ? Convert.FromBase64String(plain) : null;

    public string? SealFor(string? protectedKey, string relayPublicKey) =>
        Plain(protectedKey) is { } plain ? RelaySecretEnvelope.Seal(plain, relayPublicKey) : null;

    private string? Plain(string? protectedKey)
    {
        if (string.IsNullOrEmpty(protectedKey))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>
/// The PC de secours's copy of its prepared-session key (D22), in <c>.local/relay-assertion-key</c> under this install's
/// key ring. Beside <c>relay.json</c> rather than inside it: that file is written once, at pairing, and this arrives later
/// with a heartbeat. A new pairing deletes it — the new relay row has a new key.
/// </summary>
public sealed class RelayAssertionKeyStore
{
    public const string FileName = "relay-assertion-key";

    private readonly IDataProtector _protector;
    private readonly string _path;

    public RelayAssertionKeyStore(IDataProtectionProvider protection, string? localDir = null)
    {
        _protector = protection.CreateProtector(RelayAssertionKeys.Purpose + ".Local");
        _path = Path.Combine(localDir ?? LocalInstallPaths.LocalDir, FileName);
    }

    public bool Exists => TryLoad() is not null;

    public void Save(byte[] key) => AtomicFile.Write(_path, _protector.Protect(Convert.ToBase64String(key)));

    public byte[]? TryLoad()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(_protector.Unprotect(File.ReadAllText(_path)));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
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

/// <summary>What the PC trades tickets with (D22): its pairing and its key, re-read every few seconds; null off a PC.</summary>
public sealed class RelayLocalAssertionKeyReader : IRelayLocalAssertionKey
{
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(5);

    private readonly bool _isRelay;
    private readonly RelayCredentialStore _credentials;
    private readonly RelayAssertionKeyStore _keys;
    private readonly object _gate = new();
    private RelayLocalAssertionKey? _cached;
    private DateTime _readAtUtc = DateTime.MinValue;

    public RelayLocalAssertionKeyReader(bool isRelay, RelayCredentialStore credentials, RelayAssertionKeyStore keys)
    {
        _isRelay = isRelay;
        _credentials = credentials;
        _keys = keys;
    }

    public RelayLocalAssertionKey? Current()
    {
        if (!_isRelay)
        {
            return null;
        }

        lock (_gate)
        {
            if (DateTime.UtcNow - _readAtUtc >= ReadEvery)
            {
                var credentials = _credentials.TryLoad();
                var key = _keys.TryLoad();
                _cached = credentials is not null && key is not null
                    ? new RelayLocalAssertionKey(credentials.RelayId, credentials.ClinicId, key)
                    : null;
                _readAtUtc = DateTime.UtcNow;
            }

            return _cached;
        }
    }
}
