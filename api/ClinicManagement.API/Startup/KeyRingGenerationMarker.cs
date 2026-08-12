using ClinicManagement.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Writes which key-ring generations this deployment can read into a small file the backup sidecar stamps
/// beside every dump (<c>hosted-security-hardening</c> FR-3.9).
///
/// <para><b>The failure it prevents.</b> Restoring a dump against a key ring that never held its keys produces a
/// practice whose second factors, reminder credentials and calendar tokens are <i>all</i> silently
/// undecryptable — discovered when nobody can sign in, days later, with the working ring already overwritten.
/// Nothing about the dump itself says which ring it belongs to, so the stamp has to be put there when it is
/// taken.</para>
///
/// <para>⚠️ <b>The ring is NEVER mounted into the sidecar</b> — that is what <c>exploration.md</c> § 3.1
/// forbids, and it is the whole reason this is a file rather than the sidecar asking. One archive holding both
/// the ciphertext and the key that opens it means the encryption protects nothing against the likeliest
/// exposure. The sidecar mounts this marker <b>read-only</b> and it carries key <i>ids</i>, never key material.</para>
///
/// <para>⚠️ <b>It lists EVERY key the ring holds, not only the active one, and that is the answer to the
/// staleness the story flagged.</b> Written at startup, an « active key » marker goes stale the moment the
/// framework rolls a key on its own — and a restore would then be refused over a mismatch that is not real. The
/// question a restore actually needs answered is « does the target ring contain the key this dump's data was
/// written under? », which a list answers and equality cannot. The refresh rule is therefore: <b>rewritten at
/// every startup and by <c>reprotect-secrets --rotate</c></b>; an automatic rollover between restarts can only
/// make the list <i>narrower</i> than reality, so the check errs toward refusing — the safe direction.</para>
///
/// <para>⚠️ <b>Best-effort: a failure warns and never stops the API.</b> An unwritable marker volume is an
/// operator misconfiguration, and refusing to serve a whole deployment's clinics over a restore aid would be a
/// far larger outage than the risk it guards. The consequence is stated instead: the sidecar stamps
/// <c>unknown</c>, and the restore check refuses an unknown stamp.</para>
/// </summary>
public static class KeyRingGenerationMarker
{
    /// <summary>Where the marker is written. The compose files mount a <c>keyring_marker</c> volume here.</summary>
    public const string PathKey = "DataProtection:GenerationMarkerPath";

    /// <summary>First line of the file: the generation new writes use.</summary>
    public const string ActivePrefix = "active=";

    /// <summary>One per remaining line: a generation this ring can still decrypt.</summary>
    public const string ReadablePrefix = "readable=";

    /// <summary>
    /// Writes the marker if a path is configured. Returns the file written, or null when nothing was configured
    /// or the write failed — the caller logs, and neither outcome is fatal.
    /// </summary>
    public static string? TryWrite(IServiceProvider services, IConfiguration configuration, out string? problem)
    {
        problem = null;

        var path = configuration[PathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var provider = services.GetRequiredService<IDataProtectionProvider>();
            var active = DataProtectionKeyGeneration
                .Current(provider.CreateProtector("ClinicManagement.KeyRingGeneration.Probe.v1"))
                .Id;

            var lines = new List<string> { ActivePrefix + active };

            // Every key the ring holds, so a rollover since the last restart cannot make a real ring look wrong.
            var keyManager = services.GetService<IKeyManager>();
            if (keyManager is not null)
            {
                foreach (var key in keyManager.GetAllKeys())
                {
                    lines.Add(ReadablePrefix + DataProtectionKeyGeneration.IdOf(key.KeyId));
                }
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(path, lines);
            return path;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return null;
        }
    }
}
