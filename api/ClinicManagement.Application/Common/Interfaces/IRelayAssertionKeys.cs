namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// The cloud's half of the prepared-session key (<c>clinic-pc-copy</c> D22): one secret per PC de secours, kept under
/// the cloud's key ring, and handed to that PC sealed with its own public key — the TOTP secrets' channel (D8).
/// </summary>
public interface IRelayAssertionKeys
{
    /// <summary>A new random key, already protected for storage on the relay row.</summary>
    string NewProtectedKey();

    /// <summary>The key itself, or null when the stored value cannot be read (a rotated key ring).</summary>
    byte[]? Open(string? protectedKey);

    /// <summary>The key sealed for the PC whose public key this is; null when it cannot be read.</summary>
    string? SealFor(string? protectedKey, string relayPublicKey);
}

/// <summary>The PC de secours's half: the key the cloud sealed for it, and who it is (D22). Null off a PC or before delivery.</summary>
public interface IRelayLocalAssertionKey
{
    RelayLocalAssertionKey? Current();
}

public sealed record RelayLocalAssertionKey(Guid RelayId, Guid ClinicId, byte[] Key);
